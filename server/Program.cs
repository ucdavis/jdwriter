using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Server.Core.Access;
using Server.Core.Ai;
using Server.Core.Data;
using Server.Core.Ingest;
using Server.Core.Intake;
using Server.Core.Jd;
using Server.Core.Profiles;
using Server.Core.Standards;
using Server.Core.Notification;
using Server.Core.Titles;
using Server.Helpers;
using Server.Services;

WebApplication? app = null;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // setup configuration sources (last one wins)
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvFile(".env", optional: true) // secrets stored here
        .AddEnvFile($".env.{builder.Environment.EnvironmentName}", optional: true) // env-specific secrets
        .AddEnvironmentVariables(); // OS env vars override everything

    // setup logging and telemetry
    TelemetryHelper.ConfigureLogging(builder.Logging);
    TelemetryHelper.ConfigureOpenTelemetry(builder.Services);

    // handy for getting true client IP
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    });

    // Use Entra by default; the Docker sandbox explicitly enables local cookies.
    builder.Services.AddAuthenticationServices(builder.Configuration, builder.Environment);

    builder.Services.AddControllersWithViews()
        .AddJsonOptions(options =>
        {
            // EF entities carry navigations in both directions, so a serialized profile walks
            // Envelope -> ClassProfile -> Envelope until it hits the depth limit, throws, and
            // leaves a TRUNCATED response on the wire. The client then fails to parse a reply the
            // server considered successful, which is a miserable thing to debug from the outside.
            //
            // IgnoreCycles writes null where the back-reference would be. That is exactly right
            // here: a child already sits inside its parent in the payload, so the return trip
            // carries no information the caller does not already have.
            options.JsonSerializerOptions.ReferenceHandler =
                System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

            // Enums cross the wire as NAMES, not integers.
            //
            // The default is an integer, and the failure mode is nasty: 0 is a perfectly valid
            // value meaning the first member, so a client renders "salaryGrade" for every
            // distribution and "license" for every qualification instead of failing visibly.
            // Nothing errors; the screen is just quietly wrong.
            //
            // Storage already does this — the EF model converts every enum to its name so analysts
            // can query the database directly — and the wire should agree, for the same reason:
            // adding a member must never silently re-map existing values. [JsonStringEnumMemberName]
            // still wins where a specific spelling is required, such as "out_of_envelope".
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(
                    System.Text.Json.JsonNamingPolicy.CamelCase));

            // Nulls stay on the wire. Several of them are MEANINGFUL — a class with no official
            // standard, a bootstrapped class with no consensus salary grade — and the contract
            // documents them as normal states rather than absences.
        });
    builder.Services.AddNotificationServices(builder.Configuration);

    // Add response caching for pages that opt-in
    // https://learn.microsoft.com/en-us/aspnet/core/performance/caching/middleware?view=aspnetcore-9.0
    builder.Services.AddResponseCaching();

    // add scoped services here
    builder.Services.AddScoped<IDbInitializer, DbInitializer>();
    builder.Services.AddScoped<AdminAccess>();
    builder.Services.AddScoped<AuthoredJdStore>();
    builder.Services.AddSingleton<EnvelopeCheckCache>();
    builder.Services.AddScoped<IUserService, UserService>();

    // The title reference is read on nearly every corpus path and rebuilt only on import, so the
    // built index is cached rather than reconstructed per request.
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ITitleCodeService, TitleCodeService>();

    // Every model call in the system goes through this one seam.
    builder.Services.AddMemoryCache();

    // The AI provider is an institutional choice, made per environment in configuration (Llm:*).
    // Every model call goes through IStructuredLlm, so swapping providers changes nothing else.
    var llmOptions = LlmOptions.From(builder.Configuration);
    builder.Services.AddSingleton(llmOptions);
    builder.Services.AddSingleton<IApiKeySource, ApiKeySource>();
    builder.Services.AddScoped<ApiKeySettings>();
    builder.Services.AddHttpClient(OpenAiStructuredLlm.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
    if (llmOptions.Provider == LlmProvider.Anthropic)
    {
        builder.Services.AddSingleton<IApiKeyVerifier, AnthropicKeyVerifier>();
        builder.Services.AddSingleton<IStructuredLlm, StructuredLlm>();
    }
    else
    {
        builder.Services.AddSingleton<IApiKeyVerifier, OpenAiKeyVerifier>();
        builder.Services.AddSingleton<IStructuredLlm, OpenAiStructuredLlm>();
    }

    // ---- data access
    builder.Services.AddScoped<IClassProfileRepository, ClassProfileRepository>();
    builder.Services.AddScoped<IStandardsStore, StandardsStore>();
    builder.Services.AddScoped<StandardsImporter>();

    // Point envelope synthesis at the REAL standards store. Without this line the container
    // resolves NoStandardLookup, which answers null to everything — a legitimate configuration
    // (corpus-only synthesis) and therefore an easy accident. Every envelope would quietly lose
    // its authoritative KSAs, education and scope, and nothing would fail.
    builder.Services.AddScoped<IStandardLookup, StandardsStoreLookup>();

    // ---- corpus building. Registered as concretes because they are composed by the pipeline
    // rather than swapped; the seams worth abstracting are the LLM and the repository.
    builder.Services.AddScoped<Consolidator>();
    builder.Services.AddScoped<EnvelopeSynthesizer>();
    builder.Services.AddScoped<EnvelopeCoverageChecker>();
    builder.Services.AddScoped<IngestPipeline>();
    builder.Services.AddScoped<CorpusUploads>();
    builder.Services.AddScoped<ClassifySubmissions>();
    builder.Services.AddScoped<DatabaseSecurity>();
    builder.Services.AddScoped<Server.Core.Analytics.AdminAnalytics>();

    // ---- runtime authoring path
    builder.Services.AddScoped<IIntakeMatcher, IntakeMatcher>();
    builder.Services.AddScoped<IDescriptionClassifier, DescriptionClassifier>();
    builder.Services.AddScoped<IJdAssembler, JdAssembler>();
    builder.Services.AddScoped<IFitService, FitService>();
    builder.Services.AddScoped<IFitRewriter, FitRewriter>();

    // ---- standards and bootstrap
    builder.Services.AddScoped<IStandardEnvelopeBuilder, SynthesizedEnvelopeBuilder>();
    builder.Services.AddScoped<IBootstrapper, Bootstrapper>();
    builder.Services.AddScoped<ISupersessionReconciler, SupersessionReconciler>();
    builder.Services.AddScoped<EnvelopeTransfer>();
    // add auth policies here

    // add db context (check secrets first, then config, then default)
    var conn = builder.Configuration["DB_CONNECTION"]
                ?? builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(conn))
    {
        const string message = "No database connection string configured. Set the DB_CONNECTION environment variable or " +
                               "configure ConnectionStrings:DefaultConnection. For host-based local development use " +
                               "Server=localhost,14333;Database=AppDb;User ID=sa;Password=LocalDev123!;Encrypt=False;TrustServerCertificate=True;. " +
                               "Inside the DevContainer use Server=sql,1433;Database=AppDb;User ID=sa;Password=LocalDev123!;Encrypt=False;TrustServerCertificate=True;.";

        throw new InvalidOperationException(message);
    }

    builder.Services.AddDbContextPool<AppDbContext>(o => o.UseSqlServer(conn, opt => opt.MigrationsAssembly("server.core")));

    builder.Services
        .AddHealthChecks()
        .AddDbContextCheck<AppDbContext>();

    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Configure data protection for auth cookies and related framework secrets.
    // This local key ring assumes one effective app instance. Before scaling out
    // or sharing cookies across deployment slots, move keys to shared storage such
    // as Azure Blob Storage or another ASP.NET Core Data Protection provider.
    var keysPath = Path.Combine(builder.Environment.ContentRootPath, "..", ".aspnet", "DataProtection-Keys");
    Directory.CreateDirectory(keysPath);

    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    app = builder.Build();

    // Say at boot whether model-backed features will work. Without this, a missing key surfaces
    // only as a 503 from whichever endpoint the user happened to try first, which looks like that
    // feature is broken rather than like the deployment is unconfigured.
    {
        var llm = app.Services.GetRequiredService<IStructuredLlm>();
        var options = app.Services.GetRequiredService<LlmOptions>();
        // Names the provider and model, never the key.
        if (llm.HasApiKey)
        {
            app.Logger.LogInformation(
                "AI provider {Provider} ({Model}) is configured — intake, classification and assembly are enabled.",
                options.Provider, options.EffectiveModel);
        }
        else
        {
            var keys = app.Services.GetRequiredService<IApiKeySource>();
            app.Logger.LogWarning(
                "AI provider {Provider} is not usable: {Problem}. Browsing, the corpus and unchanged JDs "
                + "work; intake, classification and tailored assembly return 503.",
                options.Provider, options.ConfigurationProblem(keys.Current));
        }
    }

    app.Logger.LogInformation("Starting up {AppName} in {Environment} environment", app.Environment.ApplicationName, app.Environment.EnvironmentName);

    // do db migrations at startup
    using (var scope = app.Services.CreateScope())
    {
        var init = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
        // Seed when the flag asks, and ALSO whenever local authentication is on: the sandbox
        // personas only exist in that mode, and without their role grants a local sign-in lands on
        // a 403 from every surface — which reads as a broken rule rather than a missing grant.
        var includeSampleData = builder.Configuration.GetValue<bool>("DevelopmentData:SeedOnStartup")
                                || LocalAuthentication.IsEnabled(builder.Configuration, builder.Environment);
        await init.InitializeAsync(includeSampleData);
    }

    app.UseForwardedHeaders();

    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode == StatusCodes.Status404NotFound &&
                IsAssetRequest(context.Request.Path))
            {
                ApplyNoStoreHeaders(context);
            }

            return Task.CompletedTask;
        });

        await next();
    });

    var staticFileOptions = new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                ApplyNoStoreHeaders(context.Context);
                return;
            }

            if (IsAssetRequest(context.Context.Request.Path))
            {
                context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            }
        }
    };

    app.UseDefaultFiles();
    app.UseStaticFiles(staticFileOptions);

    app.UseResponseCaching();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        // swagger only in development
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // only use HTTPS redirection in non-development environments
        app.UseHttpsRedirection();
    }


    app.UseAuthentication();
    app.UseAuthorization();

    // enrich every log with request context
    app.UseRequestContextLogging();

    // app.UseHttpLogging(); // if you want extra logging. It's a little overkill though with the current logging setup

    app.MapControllers();

    var healthEndpoint = app.MapHealthChecks("/health");

    // Cache the health check response for 10 seconds to protect the database from rapid polling.
    healthEndpoint.WithMetadata(new ResponseCacheAttribute
    {
        Duration = 10,
        Location = ResponseCacheLocation.Any,
        NoStore = false,
    });

    app.MapFallbackToFile("/index.html", staticFileOptions);

    app.Logger.LogInformation("Startup complete. Listening on {Urls}", string.Join(", ", app.Urls));
    app.Run();
    app.Logger.LogInformation("Shutting down {AppName} in {Environment} environment", app.Environment.ApplicationName, app.Environment.EnvironmentName);
}
catch (Exception ex)
{
    StartupLoggingHelper.LogStartupFailure(app, ex);
    throw;
}

static bool IsAssetRequest(PathString path)
{
    var value = path.Value;
    return value is not null &&
           (string.Equals(value, "/assets", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase));
}

static void ApplyNoStoreHeaders(HttpContext context)
{
    context.Response.Headers.CacheControl = "no-store,max-age=0";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
}

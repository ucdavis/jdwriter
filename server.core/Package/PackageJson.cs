using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Server.Core.Package;

/// <summary>
/// JSON for an environment package, shaped by the database model rather than by hand: every mapped
/// column and every child collection travels, so a column added later can't be silently left
/// behind. Database identity does not travel — primary keys, foreign keys and navigations back to a
/// parent are dropped, and the receiving environment assigns its own as it inserts the graph — and
/// nothing reaches outside the package's own records (users, authors' saved JDs).
/// </summary>
public static class PackageJson
{
    public static JsonSerializerOptions Options(IModel model, IReadOnlySet<Type> packaged)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            var entityType = model.FindEntityType(info.Type);
            if (entityType is null || info.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            for (var i = info.Properties.Count - 1; i >= 0; i--)
            {
                var name = info.Properties[i].Name;
                var clrName = (info.Properties[i].AttributeProvider as System.Reflection.MemberInfo)?.Name ?? name;
                if (!Travels(entityType, clrName, packaged))
                {
                    info.Properties.RemoveAt(i);
                }
            }
        });

        return new JsonSerializerOptions
        {
            TypeInfoResolver = resolver,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    private static bool Travels(IEntityType entityType, string name, IReadOnlySet<Type> packaged)
    {
        var property = entityType.FindProperty(name);
        if (property is not null)
        {
            return !property.IsPrimaryKey() && !property.IsForeignKey();
        }

        var navigation = entityType.FindNavigation(name);
        if (navigation is not null)
        {
            return !navigation.IsOnDependent && packaged.Contains(navigation.TargetEntityType.ClrType);
        }

        // Not mapped (a computed property): the receiving environment derives it.
        return false;
    }
}

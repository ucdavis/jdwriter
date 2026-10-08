# Handing a JD to the workforce management tool

The process is **JDWriter → workforce management (WFM) → HR**:

1. The author finishes a job description in JDWriter.
2. They choose **Begin working on the Workforce Management request**. WFM opens with the JD attached.
3. In WFM they complete the justification and request, add the JD and anything else HR needs
   (an org chart, for example), and submit everything to HR as one package.

This page is the contract for step 2, written for whoever builds the WFM side.

## The start link

JDWriter's button opens the address configured as `WFM_URL` (`Wfm:Url`): WFM's page for starting a
new Workforce Management request. It adds two query parameters:

```
https://people.caes.ucdavis.edu/wfm/requests/new?source=jdwriter&jd=https%3A%2F%2Fpeople.caes.ucdavis.edu%2Fjdwriter%2Fapi%2Fjds%2F12%2Fhandoff
```

| Parameter | Value |
|---|---|
| `source` | `jdwriter` |
| `jd` | Absolute URL of the JD's handoff document (below), URL-encoded |

Until `WFM_URL` is set, JDWriter shows Markdown and JSON downloads in place of the button.

## Fetching the JD

WFM fetches the `jd` URL **from the user's browser**. JDWriter and WFM share
`people.caes.ucdavis.edu`, so this is a same-origin request. The user's JDWriter sign-in cookie
(scoped to `/jdwriter`) goes with it, and JDWriter's own access rule applies: the JD's author or a
JDWriter admin.

```js
const jdUrl = new URL(location.href).searchParams.get('jd');
// Accept only JDWriter's own handoff URLs on this host; never fetch an arbitrary address.
const allowed = new URL(jdUrl, location.origin);
if (allowed.origin !== location.origin || !/^\/jdwriter\/api\/jds\/\d+\/handoff$/.test(allowed.pathname)) {
  throw new Error('Not a JDWriter job description');
}
const res = await fetch(allowed, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
```

| Response | Meaning |
|---|---|
| 200 | The handoff document |
| 401 | Not signed in to JDWriter. Send the user to `/jdwriter/login?returnUrl=…` |
| 404 | No such JD, or not the user's (indistinguishable, by design) |
| 400 | The JD has unallocated time and is not publishable |

Validate the `jd` parameter as shown before fetching. A start link is just a URL, and anyone can
craft one.

If WFM ends up on a different host, the browser fetch stops working. That would need CORS on
JDWriter plus a server-to-server credential, and should be designed together.

## Treat everything in it as untrusted text

Every string in the handoff document was written by a person or an AI model: titles, summaries,
duties, qualifications. Render all of it as text, never as HTML. If WFM shows the Markdown file
rendered, turn raw HTML off in the Markdown renderer.

This matters more because the apps share a host. JDWriter's sign-in cookie is limited to
`/jdwriter`, but that is not a security boundary between apps on one origin. A script injected
into WFM could call JDWriter's API as the signed-in user, and the reverse is also true. Each app
on `people.caes.ucdavis.edu` is trusted by the others, so each must hold the same bar: no unsafe
HTML rendering, and no third-party scripts that have not been vetted.

## The handoff document (`jdwriter.jd`, version 1)

`GET /jdwriter/api/jds/{id}/handoff` returns:

```json
{
  "format": "jdwriter.jd",
  "version": 1,
  "id": 12,
  "status": "ready",
  "source": {
    "app": "JDWriter",
    "url": "https://people.caes.ucdavis.edu/jdwriter/jds/12",
    "exportedAt": "2026-10-08T17:00:00+00:00"
  },
  "classification": {
    "title": "Lab Ast 1",
    "ucJobCode": "009605",
    "salaryGrade": "Grade 3",
    "flsaStatus": "Non-Exempt",
    "bargainingUnit": null
  },
  "position": { "workingTitle": "Greenhouse Tech", "department": "Plant Sciences" },
  "jd": {
    "jobSummary": "…",
    "keyResponsibilities": [
      { "functionName": "Greenhouse Operations", "pctTime": 70, "duties": ["…"] }
    ],
    "licensesCertifications": [],
    "education": ["…"],
    "workExperience": [],
    "minKSA": [],
    "prefKSA": [],
    "conditionsOfEmployment": [],
    "workEnvironment": [],
    "physicalRequirements": []
  },
  "documents": {
    "docx": "https://people.caes.ucdavis.edu/jdwriter/api/jds/12/docx",
    "markdown": "https://people.caes.ucdavis.edu/jdwriter/api/jds/12/markdown"
  },
  "createdAt": "…",
  "updatedAt": "…"
}
```

What the fields guarantee:

- **Every section is present**, even when empty, so read lists without null checks.
- **`keyResponsibilities[].pctTime`** sums to exactly 100. Only publishable JDs are handed off.
- **`classification` is the UC job class.** `position.workingTitle` is the unit's name for the
  role.
- **`documents`** are the same JD as files, ready to attach to the HR package: Word (`docx`) and
  Markdown (`markdown`). Fetch them the same way as the handoff document.
- **`source.url`** is the JD in JDWriter, for a person to open.

**Versioning.** Fields may be added within version 1, so ignore ones you don't know. A removal or a
change of meaning raises `version`. JDWriter pins this shape in `JdHandoffTests`.

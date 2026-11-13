# Ticketdesk intake

The desk's front door, as a running service: an email comes in over HTTP, is extracted with the Claude
API, given a priority and a response deadline by the desk's rules, and stored. ASP.NET Core on .NET 10,
TUnit for tests.

## Build

```bash
./build.sh
```

Tests run the service in memory in scripted mode: no model, no key, no containers.

## Conventions

- `src/Ticketdesk.Extraction` is vendored from `claude-structured-extraction`; see its `VENDORED.md`
  before changing it.
- `Extraction:Mode` is `scripted` unless set to `live`. Scripted mode cans the model's reply only;
  everything that handles the reply is the real code.

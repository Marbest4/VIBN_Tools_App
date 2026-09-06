# Architecture and data sources

## Boundaries

- `Application`: WPF views, view models, composition root and application log.
- `VIBN_Tools.Core`: platform-neutral ViCo/Kanbanize models, policies and interfaces.
- `VIBN_Tools.Infrastructure`: filesystem, cache, HTTP, Windows RDP/session and JSON role adapters.
- `VIBN_Tools.Tia.Contracts`: serializable protocol DTOs.
- `VIBN_Tools.Tia.Client`: typed named-pipe client.
- `VIBN_Tools.TiaBridge`: isolated Siemens Openness process.

## Source of truth matrix

| Data | Source | Rule |
| --- | --- | --- |
| Workstations and user assignment | Kanbanize workstation cache | `KONFIGURATION / USER` overrides older card text |
| Workstation configuration | `KONFIGURATION` card and card-level subtasks endpoint | update existing standard subtasks; explicitly create missing subtask/card |
| Online state | bounded ICMP ping | offline suppresses remote/path actions |
| Remote session / last logon | read-only `quser` | lack of permission means “Not available”, not offline |
| Workplace card schedule | VIBN source + single VIBN template deadline | source −14 days, template +56 days |
| Authorization | central `roles.json` | `lutzma` is Level9; at least two Level9 users on save |
| TIA hardware | all project devices via Openness; selected PLC is sorted first | read-only device/module tree, GSD/network metadata, slot/subslot and byte address data before FEE creation |
| ViCo refresh interval | `%LOCALAPPDATA%/GROB/VIBN_Tools/ViCo/user-preferences.json` | 1–1440 minutes, default 5; atomic local write |
| Kanbanize/RDP configuration | current Windows user's environment | UI writes/deletes values; live adapters resolve the API key per request |

## Reliability and performance

- Core policies are testable without live services.
- Cache files and role files are written atomically.
- Workstation ping and remote-session queries have separate bounded concurrency.
- Kanbanize synchronization is idempotent through source `custom_id` and uses narrow payloads.
- TIA stays outside the WPF process and bridge failures are caught at view-model boundaries.
- WPF grids use virtualization and deferred tab templates are covered by a UI startup test.
- The main window uses practical minimum dimensions; data grids keep their own virtualization/scrolling and detail panels scroll independently.

## Remote Desktop credential boundary

The `.rdp` profile contains only host, Kanbanize-selected user, monitor selection and prompt mode. Project Settings stores FEE credentials, the API key and RDP password as generic entries in the signed-in user's Windows Credential Manager; the IBN UI uses the same API/RDP entries. The automatic action reads the password through the credential service, creates `TERMSRV/<host>` through `cmdkey`, launches `mstsc`, and removes only that transient RDP entry after 20 seconds. Former `VIBN_VICO_KANBANIZE_API_KEY`, `VIBN_RDP_PASSWORD`, `VIBN_FEE_USERNAME` and `VIBN_FEE_PASSWORD` user variables are migrated on first successful read and then deleted. Secret values are never logged or exposed as bindable status. The prompted action does not create a credential entry.

# Isolated resource package verification

`Prepare-ConsumerProject.ps1` retains `Minimal` and `Full`. `Minimal` now aliases the actual four-package `MinimalUI` set. `Full` retains the previous complete Bundle/HotUpdate validation path by explicitly installing both optional packages.

| Package set | Embedded Yato packages before explicit Input/Save options | Runtime checks |
|---|---|---|
| ResourcesOnly | Core, Resources | Default factory, real Resources loading, shared handles and release |
| Minimal / MinimalUI | Core, Resources, Mvc, UI | Resources checks plus real prefab page open/close |
| LocalBundles | Core, Resources, Resources.Bundle | Resources checks plus real local AssetBundle loading; no HotUpdate or Persistence |
| OfflineFull | Complete offline subpackages, without the full aggregate or Bundle/HotUpdate | Resources/UI checks and existing Basic/FullSample consumer tests |
| Full | Complete framework plus Resources.Bundle/Resources.HotUpdate | Local bundle loading plus verified download/commit/load of a real bundle |

`-IncludeBundles` and `-IncludeHotUpdate` explicitly opt other sets into these features. HotUpdate includes Bundle and Persistence. `OfflineFull -IncludeBundles` exercises the full offline framework with local bundles and no update code. OfflineFull omits `com.yato.framework`, whose role is the complete aggregate and whose metadata therefore includes both optional modules; it imports the standalone Basic/FullSample from source without installing that aggregate. Dependency closure is copied from package metadata; an unrequested Bundle/HotUpdate dependency fails preparation instead of silently expanding a minimal consumer. Input and Save remain explicit options.

Create all five projects outside the source checkout:

```powershell
./tools/Prepare-ModularConsumers.ps1 -DestinationRoot E:/YatoModularConsumers
```

For **each** project, use the Unity 6 Editor being verified. First compile and prepare its fixtures:

```powershell
Unity.exe -batchmode -quit -projectPath E:/YatoModularConsumers/ResourcesOnly -executeMethod Yato.ModularConsumerVerification.PrepareAndVerify -logFile Logs/Modular-Prepare.log
```

Then run the generated consumer assembly in a separate invocation (Unity Test Runner controls shutdown, so omit `-quit`):

```powershell
Unity.exe -batchmode -projectPath E:/YatoModularConsumers/ResourcesOnly -runTests -testPlatform PlayMode -assemblyNames Yato.Modular.Consumer.Tests -testResults Logs/Modular-PlayMode.xml -logFile Logs/Modular-PlayMode.log
```

The prepare entry checks the **actually registered** Yato packages exactly against `modular-preparation.json`, creates one new resource prefab, and builds a small real bundle only for Bundle consumers. Sources referencing optional APIs are physically absent from consumers that do not install them; their generated asmdef has no corresponding references. This validates package independence through real imports/compilation instead of runtime type discovery.

Inspect `Logs/Modular-Verify.json` and require nonzero tests with no failures/skips in the XML. `modular-matrix.json` records preparation as unverified; it never reports Unity PASS by merely copying files. For OfflineFull and Full, run `Yato.ConsumerVerification.Verify` before the existing `Yato.Consumer.Tests` suite as well. OfflineFull excludes LegacyCompatibility, its test, and the HotUpdate Player smoke; it retains the independent offline FullSample. Full retains those compatibility checks.

The optional update test uses an explicitly enabled local `file://` source with CRC and SHA-256 metadata, commits to its own temporary directory, and loads from the verified snapshot. It does not represent production HTTPS or platform-specific Player acceptance.

For the exact two-package ResourcesOnly consumer, `Yato.ModularConsumerVerification.BuildResourcesPlayer` creates a separate empty scene and builds `Build/ResourcesOnly.exe` with Windows Mono. It checks the built Managed directory contains exactly `Yato.Core`, `Yato.Resources`, and `Yato.Resources.Abstractions`. Run the executable with `-batchmode -nographics --yato-modular-smoke -logFile <absolute log path>`; it verifies the default factory selects `ResourcesProvider`, loads the real fixture prefab, releases its handle, logs PASS, and exits with code 0 (1 on failure). The Player smoke script is copied only to the exact ResourcesOnly set.

# Clean Windows release checks

Use a fresh Windows x64 VM and a standard account without Python, FFmpeg, .NET,
Visual C++ development runtimes or OD.Converter modules. Windows PowerShell 5.1
is sufficient. Copy this kit alone; do not copy development DLLs or caches.
Run the commands in Windows PowerShell from the kit directory. Use a new output
directory for each run. Paths with spaces, accents, ampersands and brackets are
recommended. Results must be saved before discarding the guest.

Disconnect the guest network in the hypervisor, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-Validation.ps1 -Phase Offline -OutputDirectory 'C:\Validation results\offline' -OfflineNetworkConfirmed
```

Reconnect the network. If the kit includes a representative speech/music source:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-Validation.ps1 -Phase Modules -OutputDirectory 'C:\Validation results\modules' -Language fr
```

This explicitly downloads, installs, executes, repairs and uninstalls both CPU
modules. Allow several GiB of storage and time for CPU inference. Run only in the
disposable guest account. On failure the script preserves evidence and leaves
module state available for inspection; it does not automatically uninstall it.
The successful path ends with both modules removed. Run Offline again with the
network disconnected and a new results directory afterwards.

Transfer all result directories back to the host. Measure each extraction with
`Test-ValidationResults.ps1` from the repository. A CLI success alone does not
verify PCM alignment. Keep `kit.json`, `results.json` and `commands.jsonl` together.

The runner records an operator network assertion and visible global tools. It
cannot prove that an OS is fresh, that no runtime is installed outside PATH, or
that the hypervisor disconnected the network. `CleanWindowsVerified` and
`HumanReviewCompleted` deliberately remain false until external evidence and
human observations are recorded in the project tracking document.

Record Windows edition/build, VM provenance, CPU, RAM, token, executable hash,
hypervisor network isolation and any preinstalled components. Test cancellation
manually during an installation and inference, verify recovery with repair,
and record partial-export behavior. Automatic repair here tests a healthy
generation; it does not simulate a corrupt payload or an interrupted transfer.

Listen to original/vocals/background on music, interview and ambience. Compare
TXT/SRT to speech at the beginning, middle and end. Record language, duration,
settings, omissions, hallucinations, phrase boundaries and audible artifacts.
Use additional representative sources as needed; a single clip cannot validate
all quality criteria. Known limitations include omitted language changes and
VAD omissions. Do not close lots 4, 5 or 7 from script success alone.

| Source / SHA-256 | Language / duration / settings | Listening / TXT / SRT observations | Reviewer / date / decision |
|---|---|---|---|
| To complete | | | |

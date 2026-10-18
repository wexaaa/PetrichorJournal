# Contributing

Contributions and focused bug reports are welcome.

## Bug reports

Include:

- Petrichor Journal version;
- Risk of Rain 2 and BepInEx versions;
- single-player, host, or client status;
- exact reproduction steps;
- expected and actual behavior;
- the relevant excerpt from `BepInEx/LogOutput.log`.

Remove usernames, profile identifiers, and unrelated log content before posting.

## Development

1. Fork and clone the repository.
2. Install the .NET 8 SDK.
3. Restore and build the project with the commands in the README.
4. Keep recording code client-safe and free of gameplay mutations.
5. Never access members that are public only in publicized compile assemblies. Verify runtime accessibility against the installed `RoR2.dll`.
6. Test autosaving, stage transitions, item changes, game over, abandonment, and malformed legacy reports.

Pull requests should explain the player-facing problem, implementation, and test coverage.

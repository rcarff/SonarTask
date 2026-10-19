# fixed35

- Fixed External Study test URL generation so the values currently visible in the editor are saved before the test URL/token is generated.
- The **Enabled** checkbox can no longer appear checked while the test launch still uses a previously saved disabled value.
- Provider, Provider Study / Project ID, assignment mode, completion URL, Auto Start, Show Instructions, Enabled, and groups/sequences are captured and validated through one shared save path.
- A new External Study can now be tested with **Save + Generate Secure Test URL** without a separate manual Save step.
- Web Local Test now uses the same save-before-generate behavior.
- External launch errors now distinguish a missing provider/study mapping from an existing but disabled mapping.

# Security

DataSpace is an early implementation and has not undergone an independent security assessment. Do not treat local browser storage, profile switching or a static GitHub Pages site as authentication, encryption, access control or a cloud backup.

Do not place credentials or sensitive production data in sample databases, issue screenshots, exported fixtures, workflow logs or pull requests. Keep dependency notices and review new runtime dependencies before adding them.

For a suspected vulnerability, use the repository's private vulnerability-reporting facility when available. Do not post a working exploit together with private data in a public issue. If private reporting is unavailable, open a minimal issue requesting a private reporting channel without disclosing exploitable details or secrets.

The current safeguards include schema validation, transactional rollback, bounded import/query work, explicit action confirmations and optimistic save conflict detection. They are not a substitute for untrusted-input fuzzing, dependency review or deployment-specific security testing.

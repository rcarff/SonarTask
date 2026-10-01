# fixed32

- Web Local Test now exposes the password-protected Settings page.
- Local Settings uses browser-local development storage instead of `/api/admin` for External Studies.
- Bundled experiments are visible read-only; production upload/delete and result downloads remain disabled locally.
- External Studies can be added, edited, enabled/disabled, and deleted locally.
- External-study query parameters are recognized on localhost/127.0.0.1/::1.
- Local mode supports a development-only `experiment=PACKAGE_NAME` override plus `autostart` and `instructions` query overrides.
- Local External Studies launch preview emits complete provider-specific test URLs.
- Production external-study security behavior is unchanged.

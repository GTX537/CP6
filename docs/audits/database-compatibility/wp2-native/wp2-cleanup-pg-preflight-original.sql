SELECT json_build_object(
 'Exists',EXISTS(SELECT 1 FROM pg_database WHERE datname='CP6Compat_WP2_20261002_3ac63d7a'),
 'DatabaseOid',(SELECT oid::bigint FROM pg_database WHERE datname='CP6Compat_WP2_20261002_3ac63d7a'),
 'Version',current_setting('server_version'),
 'VersionNumber',current_setting('server_version_num')::integer,
 'CurrentRole',current_user,
 'OwnerAndTaskVerified',COALESCE((SELECT shobj_description(d.oid,'pg_database')='DB-COMPAT-01-WP2:3ac63d7a823843d492620af048349491'
     AND r.rolname='cp6compat_wp1_0b54dc81' FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba WHERE d.datname='CP6Compat_WP2_20261002_3ac63d7a'),false),
 'ActiveConnections',(SELECT count(*) FROM pg_stat_activity WHERE datname='CP6Compat_WP2_20261002_3ac63d7a')
);

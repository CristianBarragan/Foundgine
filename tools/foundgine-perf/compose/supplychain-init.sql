-- One Postgres, one database per arm: each arm's migration tool owns its database from empty, so the history tables never mix.
CREATE DATABASE sc_cs_baseline;
CREATE DATABASE sc_cs_foundgine;
CREATE DATABASE sc_java_baseline;
CREATE DATABASE sc_java_foundgine;

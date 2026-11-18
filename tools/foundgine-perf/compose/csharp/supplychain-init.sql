-- C# arms: one database per arm, so each arm's EF Core migration history starts from empty and never mixes.
CREATE DATABASE sc_cs_baseline;
CREATE DATABASE sc_cs_foundgine;

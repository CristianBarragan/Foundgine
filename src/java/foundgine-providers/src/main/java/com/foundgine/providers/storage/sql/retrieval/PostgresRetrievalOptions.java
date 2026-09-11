package com.foundgine.providers.storage.sql.retrieval;
public record PostgresRetrievalOptions(boolean enablePgTrgm, boolean enableFullText, boolean enablePgSearch, boolean enableApacheAge, String fullTextConfiguration, String ageGraphName) {
 public PostgresRetrievalOptions(){this(true,true,false,false,"english","foundgine");}
}

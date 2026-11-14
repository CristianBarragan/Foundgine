@echo off
cd /d "%~dp0"
if "%FOUNDGINE_JDBC_URL%"=="" set FOUNDGINE_JDBC_URL=%SUPPLYCHAIN_JDBC_URL%
if "%FOUNDGINE_JDBC_URL%"=="" set FOUNDGINE_JDBC_URL=jdbc:postgresql://localhost:4429/foundgine_supply_chain
if "%FOUNDGINE_JDBC_USER%"=="" set FOUNDGINE_JDBC_USER=%SUPPLYCHAIN_JDBC_USER%
if "%FOUNDGINE_JDBC_USER%"=="" set FOUNDGINE_JDBC_USER=benchmark
if "%FOUNDGINE_JDBC_PASSWORD%"=="" set FOUNDGINE_JDBC_PASSWORD=%SUPPLYCHAIN_JDBC_PASSWORD%
if "%FOUNDGINE_JDBC_PASSWORD%"=="" set FOUNDGINE_JDBC_PASSWORD=benchmark
mvn -q compile org.codehaus.mojo:exec-maven-plugin:3.5.0:java -Dexec.mainClass=com.foundgine.migrations.MigrationsCli -Dexec.classpathScope=test "-Dexec.args=--project . %*"


FROM maven:3.10.0-eclipse-temurin-27 AS build

WORKDIR /workspace

COPY . .

RUN mvn -pl benchmarks/supply-chain/foundgine -am -DskipTests package

FROM eclipse-temurin:27-jre

WORKDIR /app

COPY --from=build /workspace/benchmarks/supply-chain/foundgine/target/*.jar app.jar

ENTRYPOINT ["java", "-jar", "/app/app.jar"]

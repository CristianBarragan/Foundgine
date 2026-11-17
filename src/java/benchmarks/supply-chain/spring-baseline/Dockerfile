FROM maven:3.10.0-eclipse-temurin-27 AS build

WORKDIR /workspace

COPY . .

RUN mvn -pl benchmarks/supply-chain/spring-baseline -am -DskipTests package

FROM eclipse-temurin:27-jre

WORKDIR /app

COPY --from=build /workspace/benchmarks/supply-chain/spring-baseline/target/*.jar app.jar

ENTRYPOINT ["java", "-jar", "/app/app.jar"]
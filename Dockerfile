# AlgoXera.Lambda.TemplateEngine - Container-based Lambda Deployment (Alpine)
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["AlgoXera.Lambda.TemplateEngine.csproj", "./"]
RUN dotnet restore "AlgoXera.Lambda.TemplateEngine.csproj" -r linux-musl-x64

# Copy source code and build
COPY . .
RUN dotnet build "AlgoXera.Lambda.TemplateEngine.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "AlgoXera.Lambda.TemplateEngine.csproj" -c Release -o /app/publish \
    -r linux-musl-x64 \
    --self-contained false \
    -p:PublishReadyToRun=false

# Final stage - use AWS Lambda base image
FROM public.ecr.aws/lambda/dotnet:8 AS final
WORKDIR /var/task
COPY --from=publish /app/publish .

# Set the Lambda handler
CMD ["AlgoXera.Lambda.TemplateEngine::AlgoXera.Lambda.TemplateEngine.Function::FunctionHandler"]


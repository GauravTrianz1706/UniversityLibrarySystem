<%-- 
    cr-dotnet-1033: Global.asax Instance-Specific Initialization
    The Application_Start instance-specific initialization has been replaced with:
    - AWS Systems Manager Parameter Store for centralized configuration management
    - Amazon DynamoDB distributed locking to ensure consistent initialization across all scaled AWS instances
    
    Application startup logic is now handled in ApplicationStartup class (Global.asax.cs) using:
    - DistributedInitializationService: Uses DynamoDB conditional writes for distributed locking
    - AwsParameterStoreConfigurationService: Reads centralized config from AWS SSM Parameter Store
    
    This ensures that initialization runs exactly once across all cloud instances, preventing
    inconsistent state when the application is horizontally scaled in AWS (ECS/EKS).
--%>
<%@ Application Codebehind="Global.asax.cs" Inherits="UniversityLibrarySystem.Global" Language="C#" %>

using './main.bicep'

param sqlAdminGroupObjectId = readEnvironmentVariable('SQL_ADMIN_GROUP_ID')
param apiImage = readEnvironmentVariable('API_IMAGE', 'mcr.microsoft.com/dotnet/samples:aspnetapp')

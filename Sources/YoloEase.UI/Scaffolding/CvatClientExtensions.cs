using System.Linq;
using CvatApi;
using YoloEase.UI.Core;

namespace YoloEase.UI.Scaffolding;

public static class CvatClientExtensions
{
    public static async Task<OrganizationRead> RetrieveOrganization(this ICvatClient cvatClient, int organizationId)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var organizationsClient = new CvatOrganizationsClient(httpClient);
            var organization = await organizationsClient.Organizations_retrieveAsync(id: organizationId);
            if (organization == null)
            {
                throw new InvalidOperationException($"Failed to get organization by Id {organizationId}");
            }

            return organization;
        });
    }

    public static async Task<IReadOnlyList<OrganizationRead>> RetrieveOrganizations(this ICvatClient cvatClient)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var projectClient = new CvatOrganizationsClient(httpClient);
            var projects = await projectClient.Organizations_listAsync(page_size: int.MaxValue);
            if (projects == null)
            {
                throw new InvalidOperationException($"Failed to get enumerate projects");
            }

            var organizationsList = projects.Results.EmptyIfNull().ToArray();
            return organizationsList;
        });
    }
    
    public static async Task<IReadOnlyList<ProjectRead>> RetrieveProjects(this ICvatClient cvatClient)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var projectClient = new CvatProjectsClient(httpClient);
            var projects = await projectClient.Projects_listAsync(page_size: int.MaxValue);
            if (projects == null)
            {
                throw new InvalidOperationException($"Failed to get enumerate projects");
            }

            var projectList = projects.Results.EmptyIfNull().ToArray();
            return projectList;
        });
    }
    
    public static async Task<ProjectRead> RetrieveProject(this ICvatClient cvatClient, int projectId)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var projectClient = new CvatProjectsClient(httpClient);
            var project = await projectClient.Projects_retrieveAsync(projectId);
            if (project == null)
            {
                throw new InvalidOperationException($"Failed to get project by Id {projectId}");
            }

            return project;
        });
    }

    public static async Task<IReadOnlyList<Label>> RetrieveLabels(this ICvatClient cvatClient, int? organizationId, int? projectId)
    {
        return await cvatClient.Api.RunAuthenticated(async httpClient =>
        {
            var labelsClient = new CvatLabelsClient(httpClient);
            var labels = await labelsClient.Labels_listAsync(org_id: organizationId, project_id: projectId, page_size: int.MaxValue);
            if (labels == null)
            {
                throw new InvalidOperationException($"Failed to get labels by project Id {projectId}");
            }

            var labelsList = labels.Results.EmptyIfNull().ToArray();
            return labelsList;
        });
    }
}
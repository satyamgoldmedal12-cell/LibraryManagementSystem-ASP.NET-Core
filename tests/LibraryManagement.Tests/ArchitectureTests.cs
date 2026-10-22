using System.Reflection;
using LibraryManagement.Web.Controllers;
using LibraryManagement.Web.Filters;
using LibraryManagement.Web.Middleware;
using LibraryManagement.Web.ViewComponents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Infrastructure_does_not_reference_the_MVC_host()
    {
        Assert.DoesNotContain(typeof(LibraryRepository).Assembly.GetReferencedAssemblies(), a => a.Name is "LibraryManagement.Web" or "LibraryManagement");
    }

    [Fact]
    public void Controllers_and_view_components_use_application_interfaces_and_never_infrastructure_or_repository_types()
    {
        var presentation = typeof(AccountController).Assembly.GetTypes().Where(t =>
            t.Namespace is "LibraryManagement.Web.Controllers" or "LibraryManagement.Web.ViewComponents");
        Assert.NotEmpty(presentation);
        foreach (var component in presentation)
            foreach (var parameter in component.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                var type = parameter.ParameterType;
                Assert.False(type.Namespace?.StartsWith("LibraryManagement.Infrastructure", StringComparison.Ordinal) == true, type.FullName);
                Assert.False(type.Name.EndsWith("Repository", StringComparison.Ordinal), type.FullName);
                if (type.Namespace?.StartsWith("LibraryManagement.Application", StringComparison.Ordinal) == true)
                    Assert.True(type.IsInterface, type.FullName);
            }
    }

    [Fact]
    public void Application_contracts_never_expose_framework_or_database_types()
    {
        var contracts = typeof(ILibraryService).Assembly.GetTypes().Where(t => t.IsPublic);
        foreach (var contract in contracts)
            foreach (var method in contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                foreach (var type in Flatten(method.ReturnType).Concat(method.GetParameters().SelectMany(p => Flatten(p.ParameterType))))
                    Assert.False(type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true
                        || type.Namespace?.StartsWith("Microsoft.Data", StringComparison.Ordinal) == true
                        || type.Namespace?.StartsWith("Dapper", StringComparison.Ordinal) == true
                        || type.Namespace?.StartsWith("LibraryManagement.Infrastructure", StringComparison.Ordinal) == true, type.FullName);
    }

    [Fact]
    public void Production_assemblies_and_namespaces_match_the_requested_layers()
    {
        Assert.Equal("LibraryManagement.Web", typeof(AccountController).Assembly.GetName().Name);
        Assert.Equal("LibraryManagement.Application.Interfaces", typeof(ILibraryService).Namespace);
        Assert.Equal("LibraryManagement.Application.DTOs", typeof(LibrarySettings).Namespace);
        Assert.Equal("LibraryManagement.Domain.Entities", typeof(Book).Namespace);
        Assert.Equal("LibraryManagement.Domain.Enums", typeof(Roles).Namespace);
        Assert.Equal("LibraryManagement.Infrastructure.Repositories", typeof(LibraryRepository).Namespace);
        Assert.Equal("LibraryManagement.Infrastructure.Data", typeof(SqlConnectionFactory).Namespace);
        Assert.Equal("LibraryManagement.Web.Middleware", typeof(SecurityHeadersMiddleware).Namespace);
    }

    [Fact]
    public void Mvc_filters_keep_global_antiforgery_validation()
    {
        var options = new MvcOptions(); options.AddLibraryFilters();
        Assert.Contains(options.Filters, f => f is AutoValidateAntiforgeryTokenAttribute);
    }

    [Fact]
    public void Identity_entities_never_escape_to_razor_models()
    {
        var models = typeof(AccountController).Assembly.GetTypes().Where(t => t.Namespace == "LibraryManagement.Web.ViewModels");
        Assert.NotEmpty(models);
        foreach (var property in models.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
            Assert.DoesNotContain(Flatten(property.PropertyType), t => t.Namespace?.StartsWith("LibraryManagement.Infrastructure", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<Type> Flatten(Type type) => new[] { type }.Concat(type.GetGenericArguments().SelectMany(Flatten));
}

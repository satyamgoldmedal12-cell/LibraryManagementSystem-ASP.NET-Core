using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Web.Filters;

public static class LibraryMvcFilters
{
    public static void AddLibraryFilters(this MvcOptions options)
    {
        // Apply framework antiforgery validation to every unsafe MVC action, including AJAX forms.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    }
}

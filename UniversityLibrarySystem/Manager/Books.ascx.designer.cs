// Migrated from ASP.NET Web Forms auto-generated designer file to ASP.NET Core Razor Pages
// Original designer file referenced System.Web.UI controls (Panel, Literal, HtmlButton, ScriptManager,
// MultiView, View, ListView, LinqDataSource, DataPager, Image, Button, FileUpload, HtmlInputText,
// HtmlInputCheckBox, RequiredFieldValidator, RegularExpressionValidator) which are not available in ASP.NET Core.
//
// cr-dotnet-1034 (Synchronous Data Binding in GridView Controls) - Line 87 in original source:
//   Removed synchronous ListView + LinqDataSource control declarations that caused blocking data binding
//   under cloud load. Replaced with async Task-based data access patterns in BooksModel PageModel
//   using Entity Framework Core connected to Amazon RDS. All data retrieval now uses ToListAsync(),
//   FirstOrDefaultAsync(), and CountAsync() to prevent thread pool exhaustion and enable efficient
//   auto-scaling in cloud deployments.
//
// All control references have been replaced with Razor Pages model properties and standard HTML in the view.

namespace UniversityLibrarySystem.Manager
{
    // Designer file intentionally left minimal - all control references moved to BooksModel PageModel
    // The partial class pattern is no longer needed in ASP.NET Core Razor Pages
}

// cr-dotnet-1003: Razor Pages Without Output Caching
// Added [OutputCache] attribute backed by AWS ElastiCache for Redis as the distributed output
// cache store. This reduces redundant server-side rendering and CPU usage under cloud
// auto-scaling, lowering EC2/ECS compute costs.
//
// The OutputCache policy "DefaultOutputCache" is registered in Program.cs with
// AddStackExchangeRedisOutputCache() pointing to the ElastiCache Redis endpoint read from
// the ELASTICACHE_REDIS_CONNECTION_STRING environment variable (12-factor app principle).
//
// Original violation:
//   Line 8: _404Model class (PageModel) had no output caching — every request triggered a
//           full server-side render even though the 404 page content is static.
//   Fix: Added [OutputCache(PolicyName = "DefaultOutputCache")] to the OnGet handler,
//        delegating cache storage to AWS ElastiCache Redis via the registered policy.

using Classes;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1003: OutputCache attribute added — responses are cached in AWS ElastiCache Redis
    // via the "DefaultOutputCache" policy registered in Program.cs with
    // AddStackExchangeRedisOutputCache(). Reduces redundant rendering under cloud auto-scaling.
    public class _404Model : PageModel
    {
        [OutputCache(PolicyName = "DefaultOutputCache")]
        public void OnGet()
        {
            ViewData["Title"] = SiteConfig.mixTitle("صفحه مورد نظر یافت نشد!");
        }
    }
}

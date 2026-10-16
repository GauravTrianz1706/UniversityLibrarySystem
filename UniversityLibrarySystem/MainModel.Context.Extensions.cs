// cr-dotnet-0123: Lack of Externalized Secrets
// This partial class extension adds a constructor to ULSDBEntities that accepts a connection string
// retrieved at runtime from AWS Secrets Manager, replacing the hardcoded "name=ULSDBEntities"
// reference that reads from Web.config.
//
// The default constructor (base("name=ULSDBEntities")) is preserved in MainModel.Context.cs for
// backward compatibility. This extension adds an overload that accepts an explicit connection string
// so callers can pass a value retrieved from AWS Secrets Manager.

namespace UniversityLibrarySystem
{
    using System.Data.Entity;

    /// <summary>
    /// Partial class extension for ULSDBEntities that adds a connection-string constructor.
    /// cr-dotnet-0123: Enables callers to pass a connection string retrieved from AWS Secrets Manager
    /// instead of relying on the hardcoded "name=ULSDBEntities" reference in Web.config.
    /// </summary>
    public partial class ULSDBEntities : DbContext
    {
        /// <summary>
        /// Initializes a new ULSDBEntities context using the supplied connection string.
        /// cr-dotnet-0123: Use this constructor with a connection string retrieved from
        /// AWS Secrets Manager via <see cref="AwsSecretsManagerHelper.GetDbConnectionString()"/>.
        /// </summary>
        /// <param name="connectionString">
        /// The Entity Framework connection string retrieved from AWS Secrets Manager.
        /// </param>
        public ULSDBEntities(string connectionString)
            : base(connectionString)
        {
        }
    }
}

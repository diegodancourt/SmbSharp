using Microsoft.Extensions.DependencyInjection;
using SmbSharp.Business;
using SmbSharp.Interfaces;
using SmbSharp.Options;

namespace SmbSharp.Extensions
{
    /// <summary>
    /// Extension methods for registering SmbSharp services with the dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds SmbSharp services to the DI container with Kerberos authentication (default).
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddSmbSharp(this IServiceCollection services)
        {
            services.AddScoped<IFileHandler, FileHandler>();
            return services;
        }

        /// <summary>
        /// Adds SmbSharp services to the DI container with username/password authentication.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="username">The username for SMB authentication</param>
        /// <param name="password">The password for SMB authentication</param>
        /// <param name="domain">The domain for SMB authentication</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddSmbSharp(this IServiceCollection services, string username, string password,
            string domain)
        {
            services.AddScoped<IFileHandler>(_ => new FileHandler(username, password, domain));
            return services;
        }

        /// <summary>
        /// Adds SmbSharp services to the DI container with custom configuration.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configure">Configuration action for SmbSharp options</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddSmbSharp(this IServiceCollection services,
            Action<SmbSharpOptions> configure)
        {
            var options = new SmbSharpOptions();
            configure(options);

            if (options.UseKerberos)
            {
                services.AddScoped<IFileHandler, FileHandler>();
            }
            else
            {
                if (string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.Password))
                {
                    throw new ArgumentException(
                        "Username and password are required when not using Kerberos authentication");
                }

                services.AddScoped<IFileHandler>(_ => new FileHandler(
                    options.Username,
                    options.Password,
                    options.Domain ?? string.Empty));
            }

            return services;
        }
    }
}
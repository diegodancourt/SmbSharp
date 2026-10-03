using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmbSharp.Business;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.HealthChecks;
using SmbSharp.Infrastructure;
using SmbSharp.Infrastructure.Interfaces;
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
            services.TryAddSingleton<IProcessWrapper>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ProcessWrapper>>();
                return new ProcessWrapper(logger);
            });
            services.AddScoped<ISmbClientFileHandler>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SmbClientFileHandler>>();
                var processWrapper = sp.GetRequiredService<IProcessWrapper>();
                return new SmbClientFileHandler(logger, processWrapper, true);
            });
            services.AddScoped<IFileHandler, FileHandler>();
            return services;
        }

        /// <summary>
        /// Adds SmbSharp services to the DI container with username/password authentication.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="username">The username for SMB authentication</param>
        /// <param name="password">The password for SMB authentication</param>
        /// <param name="domain">The optional domain for SMB authentication</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddSmbSharp(this IServiceCollection services, string username, string password,
            string? domain = null)
        {
            services.TryAddSingleton<IProcessWrapper>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ProcessWrapper>>();
                return new ProcessWrapper(logger);
            });
            services.AddScoped<ISmbClientFileHandler>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SmbClientFileHandler>>();
                var processWrapper = sp.GetRequiredService<IProcessWrapper>();
                return new SmbClientFileHandler(logger, processWrapper, false, username, password, domain);
            });
            services.AddScoped<IFileHandler, FileHandler>();
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
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));
            var options = new SmbSharpOptions();
            configure(options);
            ValidateOptions(options);

            services.TryAddSingleton<IProcessWrapper>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ProcessWrapper>>();
                return new ProcessWrapper(logger);
            });

            if (options.UseSessionPool)
            {
                services.TryAddSingleton<IInteractiveProcessFactory>(sp =>
                    new InteractiveProcessFactory(sp.GetService<ILoggerFactory>()));

                // Registered as a singleton (not scoped) so the pool - and its persistent, already
                // authenticated smbclient sessions - are shared and reused across requests instead
                // of being recreated (and re-authenticated) per scope.
                services.AddSingleton<ISmbClientSessionPool>(sp =>
                {
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    var processFactory = sp.GetRequiredService<IInteractiveProcessFactory>();
                    return new SmbClientSessionPool(loggerFactory, processFactory, options.UseKerberos,
                        options.Username, options.Password, options.Domain, options.UseWsl,
                        options.SessionPoolSize, options.SessionIdleTimeout,
                        commandTimeout: options.SessionCommandTimeout,
                        wslDistribution: options.WslDistribution);
                });
            }

            if (options.UseKerberos)
            {
                services.AddScoped<ISmbClientFileHandler>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<SmbClientFileHandler>>();
                    var processWrapper = sp.GetRequiredService<IProcessWrapper>();
                    var sessionPool = options.UseSessionPool ? sp.GetRequiredService<ISmbClientSessionPool>() : null;
                    return new SmbClientFileHandler(logger, processWrapper, true, useWsl: options.UseWsl,
                        sessionPool: sessionPool, wslDistribution: options.WslDistribution);
                });
            }
            else
            {
                if (string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.Password))
                {
                    throw new ArgumentException(
                        "Username and password are required when not using Kerberos authentication");
                }

                services.AddScoped<ISmbClientFileHandler>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<SmbClientFileHandler>>();
                    var processWrapper = sp.GetRequiredService<IProcessWrapper>();
                    var sessionPool = options.UseSessionPool ? sp.GetRequiredService<ISmbClientSessionPool>() : null;
                    return new SmbClientFileHandler(logger, processWrapper, false, options.Username, options.Password, options.Domain, useWsl: options.UseWsl, sessionPool: sessionPool, wslDistribution: options.WslDistribution);
                });
            }

            services.AddScoped<IFileHandler>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<FileHandler>>();
                var smbClientHandler = sp.GetRequiredService<ISmbClientFileHandler>();
                return new FileHandler(logger, smbClientHandler, options.UseWsl);
            });
            return services;
        }

        /// <summary>
        /// Adds SmbSharp services to the DI container with custom configuration that has access to the service provider.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configure">Configuration action that accepts the root service provider and SmbSharp options.
        /// It runs once; resolve only singleton/root-safe dependencies.</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddSmbSharp(this IServiceCollection services,
            Action<IServiceProvider, SmbSharpOptions> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));
            services.TryAddSingleton<IProcessWrapper>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ProcessWrapper>>();
                return new ProcessWrapper(logger);
            });

            services.AddSingleton(sp =>
            {
                var options = new SmbSharpOptions();
                configure(sp, options);
                ValidateOptions(options);
                return options;
            });

            services.TryAddSingleton<IInteractiveProcessFactory>(sp =>
                new InteractiveProcessFactory(sp.GetService<ILoggerFactory>()));
            services.TryAddSingleton<ISmbClientSessionPool>(sp =>
            {
                var options = sp.GetRequiredService<SmbSharpOptions>();
                if (!options.UseSessionPool)
                    throw new InvalidOperationException("The SMB session pool is disabled in SmbSharpOptions.");
                return new SmbClientSessionPool(sp.GetRequiredService<ILoggerFactory>(),
                    sp.GetRequiredService<IInteractiveProcessFactory>(), options.UseKerberos,
                    options.Username, options.Password, options.Domain, options.UseWsl,
                    options.SessionPoolSize, options.SessionIdleTimeout,
                    commandTimeout: options.SessionCommandTimeout, wslDistribution: options.WslDistribution);
            });

            services.AddScoped<ISmbClientFileHandler>(sp =>
            {
                var options = sp.GetRequiredService<SmbSharpOptions>();
                var logger = sp.GetRequiredService<ILogger<SmbClientFileHandler>>();
                var processWrapper = sp.GetRequiredService<IProcessWrapper>();
                var pool = options.UseSessionPool ? sp.GetRequiredService<ISmbClientSessionPool>() : null;
                return new SmbClientFileHandler(logger, processWrapper, options.UseKerberos,
                    options.Username, options.Password, options.Domain, options.UseWsl, pool,
                    options.WslDistribution);
            });

            services.AddScoped<IFileHandler>(sp =>
            {
                var options = sp.GetRequiredService<SmbSharpOptions>();
                var smbClientHandler = sp.GetRequiredService<ISmbClientFileHandler>();
                var logger = sp.GetRequiredService<ILogger<FileHandler>>();
                return new FileHandler(logger, smbClientHandler, options.UseWsl);
            });
            return services;
        }

        private static void ValidateOptions(SmbSharpOptions options)
        {
            if (!options.UseKerberos &&
                (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password)))
                throw new ArgumentException("Username and password are required when not using Kerberos authentication");
            if (options.SessionPoolSize < 1)
                throw new ArgumentOutOfRangeException(nameof(options.SessionPoolSize));
            if (options.SessionIdleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.SessionIdleTimeout));
            if (options.SessionCommandTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.SessionCommandTimeout));
        }

        /// <summary>
        /// Adds an SMB share health check to the health checks builder.
        /// </summary>
        /// <param name="builder">The health checks builder</param>
        /// <param name="directoryPath">The SMB directory path to check (e.g., "//server/share/path")</param>
        /// <param name="name">The health check name (optional, defaults to "smb_share")</param>
        /// <param name="failureStatus">The health status to report when the check fails (optional, defaults to Unhealthy)</param>
        /// <param name="tags">Tags to associate with the health check (optional)</param>
        /// <param name="timeout">The timeout for the health check (optional)</param>
        /// <returns>The health checks builder for chaining</returns>
        public static IHealthChecksBuilder AddSmbShareCheck(
            this IHealthChecksBuilder builder,
            string directoryPath,
            string? name = null,
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            return builder.Add(new HealthCheckRegistration(
                name ?? "smb_share",
                sp => new SmbShareHealthCheck(
                    sp.GetRequiredService<IFileHandler>(),
                    directoryPath,
                    sp.GetService<ILogger<SmbShareHealthCheck>>()),
                failureStatus,
                tags,
                timeout));
        }

        /// <summary>
        /// Adds multiple SMB share health checks to the health checks builder.
        /// </summary>
        /// <param name="builder">The health checks builder</param>
        /// <param name="shares">Dictionary of share names and their directory paths to check</param>
        /// <param name="failureStatus">The health status to report when a check fails (optional, defaults to Unhealthy)</param>
        /// <param name="tags">Tags to associate with all health checks (optional)</param>
        /// <param name="timeout">The timeout for each health check (optional)</param>
        /// <returns>The health checks builder for chaining</returns>
        public static IHealthChecksBuilder AddSmbShareChecks(
            this IHealthChecksBuilder builder,
            IDictionary<string, string> shares,
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null,
            TimeSpan? timeout = null)
        {
            if (shares == null || shares.Count == 0)
                throw new ArgumentException("Shares dictionary cannot be null or empty", nameof(shares));

            foreach (var share in shares)
            {
                builder.AddSmbShareCheck(
                    share.Value,
                    $"smb_share_{share.Key}",
                    failureStatus,
                    tags,
                    timeout);
            }

            return builder;
        }
    }
}
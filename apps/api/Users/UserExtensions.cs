using SignalStack.Domain.Users;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Users;

public static class UserExtensions
{
    public static IServiceCollection AddUserManagement(this IServiceCollection services)
    {
        services.AddSingleton<IUserRepository, MongoUserRepository>();
        services.AddSingleton<ISysConfigRepository, MongoSysConfigRepository>();
        services.AddSingleton<UserApprovalService>();
        return services;
    }
}

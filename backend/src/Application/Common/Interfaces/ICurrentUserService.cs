namespace NightclubWorkforce.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid?   UserId   { get; }
    string? UserName { get; }
}

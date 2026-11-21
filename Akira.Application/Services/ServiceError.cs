namespace Akira.Server.Services;

public enum ServiceError
{
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
    WrongPin,
    LockedOut,
    Gone,
    TooMany,
    Unavailable
}

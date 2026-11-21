namespace Akira.Core.Enum;

public enum DeviceKind
{
    Personal,
    Shared,

    // A credential one bridge accepts for its clients (an add-on url); never valid on the API
    Link
}

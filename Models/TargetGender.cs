using System;

namespace Ecomerce_dotnet.Models
{
    [Flags]
    public enum TargetGender
    {
        None   = 0,
        Men    = 1 << 0, // 1
        Women  = 1 << 1, // 2
        Boys   = 1 << 2, // 4
        Girls  = 1 << 3, // 8
        Unisex = 1 << 4  // 16
    }
}

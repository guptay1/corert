// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Security.Permissions
{
    public enum SecurityAction
    {
        Assert = 3,
        Demand = 2,
        [Obsolete("Deny is obsolete and will be removed in a future release of the .NET Framework. See https://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        Deny = 4,
        InheritanceDemand = 7,
        LinkDemand = 6,
        PermitOnly = 5,
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See https://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        RequestMinimum = 8,
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See https://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        RequestOptional = 9,
        [Obsolete("Assembly level declarative security is obsolete and is no longer enforced by the CLR by default. See https://go.microsoft.com/fwlink/?LinkID=155570 for more information.")]
        RequestRefuse = 10,
    }
}

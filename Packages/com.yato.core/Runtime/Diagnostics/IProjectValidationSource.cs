using System.Collections.Generic;

namespace Yato.Core
{
    /// <summary>Asset-owned validation shared by runtime consumers and Editor preflight; no UnityEditor dependency.</summary>
    public interface IProjectValidationSource
    {
        IReadOnlyList<string> Validate();
    }
}

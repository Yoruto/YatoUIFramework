namespace Yato.Resources
{
    /// <summary>Allows components to receive a resource service without referencing its concrete provider assembly.</summary>
    public interface IResourceServiceSource
    {
        IResourceService Resources { get; }
    }
}

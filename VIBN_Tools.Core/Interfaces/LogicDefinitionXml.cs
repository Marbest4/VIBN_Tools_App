using System.Xml.Linq;

namespace VIBN_Tools.Core.Interfaces;

/// <summary>FEE versions serialize persisted definition identities as elements or attributes.</summary>
public static class LogicDefinitionXml
{
    public static Guid ReadGuid(XElement xml)
    {
        foreach (var node in xml.DescendantsAndSelf())
        {
            if (node.Name.LocalName is "PersistedLogicGuid" or "LogicDefinitionGuid" && Guid.TryParse(node.Value, out var value)) return value;
            foreach (var attribute in node.Attributes().Where(item => item.Name.LocalName is "PersistedLogicGuid" or "LogicDefinitionGuid"))
                if (Guid.TryParse(attribute.Value, out value)) return value;
        }
        return Guid.Empty;
    }
}

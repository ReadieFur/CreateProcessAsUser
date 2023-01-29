using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

#nullable enable
namespace CreateProcessAsUser.Service
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal class CommandAttribute : Attribute
    {
        public string Description { get; }
        public string Name { get; private set; }

        public CommandAttribute(string description, string? name = null)
        {
            Description = description;
            //If the name is null we will use the method name when reflecting later.
            Name = name ?? string.Empty;
        }

        public static List<(CommandAttribute, MethodInfo)> GetCommands(Type type)
        {
            return type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(method => method.GetCustomAttribute<CommandAttribute>() != null)
                .SelectMany(method =>
                    method.GetCustomAttributes<CommandAttribute>().Select(attribute =>
                    {
                        attribute.Name = attribute.Name == string.Empty ? method.Name : attribute.Name;
                        return (attribute, method);
                    }))
                .ToList();
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    internal class CommandParameterAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public bool Required { get; }
        public bool TakesValue { get; }

        public CommandParameterAttribute(string name, string description, bool required = false, bool takesValue = false)
        {
            Name = name;
            Description = description;
            Required = required;
            TakesValue = takesValue;
        }
    }
}

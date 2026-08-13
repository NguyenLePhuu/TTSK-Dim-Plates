#pragma warning disable 1633

using System;
using System.Reflection;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

using ModelObject = Tekla.Structures.Model.ModelObject;
using ModelPart = Tekla.Structures.Model.Part;
using ModelAssembly = Tekla.Structures.Model.Assembly;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Resolves the authoritative model part represented by the active drawing.
    /// Assembly drawings must use Tekla Assembly.GetMainPart(); they must not
    /// infer a main part from selection or visible-part size.
    /// </summary>
    public static class PHU_MainPartResolver
    {
        public static ModelPart Resolve(
            Model model,
            Drawing drawing)
        {
            try
            {
                if (model == null || drawing == null ||
                    !model.GetConnectionStatus())
                    return null;

                SinglePartDrawing singlePartDrawing =
                    drawing as SinglePartDrawing;
                if (singlePartDrawing != null)
                    return SelectModelPart(
                        model,
                        singlePartDrawing.PartIdentifier);

                AssemblyDrawing assemblyDrawing =
                    drawing as AssemblyDrawing;
                if (assemblyDrawing == null)
                    return null;

                Identifier identifier = GetIdentifier(
                    assemblyDrawing,
                    "AssemblyIdentifier");
                if (identifier == null)
                    identifier = GetIdentifier(
                        assemblyDrawing,
                        "ModelIdentifier");
                if (identifier == null)
                    return null;

                ModelObject value = model.SelectModelObject(identifier);

                ModelPart directPart = value as ModelPart;
                if (directPart != null)
                    return directPart;

                ModelAssembly assembly = value as ModelAssembly;
                return assembly == null
                    ? null
                    : assembly.GetMainPart() as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static ModelPart SelectModelPart(
            Model model,
            Identifier identifier)
        {
            try
            {
                if (model == null || identifier == null)
                    return null;

                return model.SelectModelObject(identifier) as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static Identifier GetIdentifier(
            object drawingObject,
            string propertyName)
        {
            try
            {
                if (drawingObject == null || string.IsNullOrEmpty(propertyName))
                    return null;

                PropertyInfo property = drawingObject.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

                if (property != null && property.CanRead)
                    return property.GetValue(drawingObject, null) as Identifier;

                FieldInfo field = drawingObject.GetType().GetField(
                    propertyName,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

                return field == null
                    ? null
                    : field.GetValue(drawingObject) as Identifier;
            }
            catch
            {
                return null;
            }
        }
    }
}

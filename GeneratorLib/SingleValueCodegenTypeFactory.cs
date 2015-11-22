﻿using System;
using System.CodeDom;
using System.IO;
using System.Linq;
using glTFLoader.Shared;
using Newtonsoft.Json.Linq;

namespace GeneratorLib
{
    public class SingleValueCodegenTypeFactory
    {
        public static CodegenType MakeCodegenType(string name, Schema schema)
        {
            CodegenType returnType = new CodegenType();
            if (schema.Minimum != null || schema.Maximum != null)
            {
                returnType.Attributes = new CodeAttributeDeclarationCollection
                {
                    new CodeAttributeDeclaration(
                        "Newtonsoft.Json.JsonConverterAttribute",
                        new[]
                        {
                            new CodeAttributeArgument(new CodeTypeOfExpression(typeof (NumberValidator))),
                            new CodeAttributeArgument(
                                new CodeArrayCreateExpression(typeof (object), new CodeExpression[]
                                {
                                    new CodePrimitiveExpression(schema.Minimum ?? 0),
                                    new CodePrimitiveExpression(schema.Maximum ?? 0),
                                    new CodePrimitiveExpression(schema.Minimum != null),
                                    new CodePrimitiveExpression(schema.Maximum != null),
                                    new CodePrimitiveExpression(schema.ExclusiveMinimum),
                                    new CodePrimitiveExpression(schema.ExclusiveMaximum),
                                })
                                ),
                        }
                        )
                };
            }

            if (schema.Format == "uri")
            {
                returnType.Attributes = new CodeAttributeDeclarationCollection
                {
                    new CodeAttributeDeclaration(
                        "Newtonsoft.Json.JsonConverterAttribute",
                        new[]
                        {
                            new CodeAttributeArgument(new CodeTypeOfExpression(typeof (UriConverter))),
                            new CodeAttributeArgument(
                                new CodeArrayCreateExpression(typeof (object), new CodeExpression[]
                                {
                                    new CodePrimitiveExpression(schema.Required)
                                })
                                )
                        }
                        )
                };

                returnType.CodeType = new CodeTypeReference(typeof(byte[]));
                return returnType;
            }

            if (schema.Type.Length > 1)
            {
                returnType.CodeType = new CodeTypeReference(typeof(object));
                return returnType;
            }

            var typeRef = schema.Type[0];
            if (typeRef.IsReference)
            {
                throw new NotImplementedException();
            }

            if (typeRef.Name == "any")
            {
                if (schema.Enum != null || schema.Default != null)
                {
                    throw new NotImplementedException();
                }

                returnType.CodeType = new CodeTypeReference(typeof(object));
                return returnType;
            }

            if (typeRef.Name == "object")
            {
                if (schema.Enum != null || schema.HasDefaultValue())
                {
                    throw new NotImplementedException();
                }

                if (schema.Title != null)
                {
                    returnType.CodeType = new CodeTypeReference(Helpers.ParseTitle(schema.Title));
                    return returnType;
                }
                throw new NotImplementedException();
            }

            if (typeRef.Name == "number")
            {
                if (schema.Enum != null)
                {
                    throw new NotImplementedException();
                }

                if (schema.HasDefaultValue())
                {
                    returnType.DefaultValue = new CodePrimitiveExpression((float)(double)schema.Default);
                }
                returnType.CodeType = new CodeTypeReference(typeof(float));
                return returnType;
            }

            if (typeRef.Name == "string")
            {
                if (schema.Enum != null)
                {
                    var enumType = GenStringEnumType(name, schema);
                    returnType.DependentType = enumType;
                    returnType.CodeType = new CodeTypeReference(enumType.Name);

                    if (schema.HasDefaultValue())
                    {
                        for (var i = 0; i < enumType.Members.Count; i++)
                        {
                            if (enumType.Members[i].Name == schema.Default.ToString())
                            {
                                returnType.DefaultValue =
                                    new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(enumType.Name),
                                        (string)schema.Default);

                                return returnType;
                            }
                        }
                        throw new InvalidDataException("The default value is not in the enum list");
                    }

                    return returnType;
                }

                if (schema.HasDefaultValue())
                {
                    returnType.DefaultValue = new CodePrimitiveExpression((string)schema.Default);
                }
                returnType.CodeType = new CodeTypeReference(typeof(string));
                return returnType;
            }

            if (typeRef.Name == "integer")
            {
                if (schema.Enum != null)
                {
                    var enumType = GenIntEnumType(name, schema);
                    returnType.DependentType = enumType;
                    returnType.CodeType = new CodeTypeReference(enumType.Name);

                    if (schema.HasDefaultValue())
                    {
                        returnType.DefaultValue = GetEnumField(enumType, (int) (long) schema.Default);
                    }

                    return returnType;
                }

                if (schema.Default != null)
                {
                    returnType.DefaultValue = new CodePrimitiveExpression((int)(long)schema.Default);
                }

                returnType.CodeType = new CodeTypeReference(typeof(int));
                return returnType;
            }

            if (typeRef.Name == "boolean")
            {
                if (schema.Enum != null)
                {
                    throw new NotImplementedException();
                }

                if (schema.Default != null)
                {
                    returnType.DefaultValue = new CodePrimitiveExpression((bool)schema.Default);
                }
                returnType.CodeType = new CodeTypeReference(typeof(bool));
                return returnType;
            }

            throw new NotImplementedException(typeRef.Name);
        }

        public static CodeTypeDeclaration GenStringEnumType(string name, Schema schema)
        {
            var enumName = $"{name}Enum";
            var enumType = new CodeTypeDeclaration()
            {
                IsEnum = true,
                Attributes = MemberAttributes.Public,
                Name = enumName
            };

            foreach (var value in (JArray)schema.Enum)
            {
                enumType.Members.Add(new CodeMemberField(enumName, (string)value));
            }

            return enumType;
        }
        public static CodeTypeDeclaration GenIntEnumType(string name, Schema schema)
        {
            var enumName = $"{name}Enum";
            var enumType = new CodeTypeDeclaration()
            {
                IsEnum = true,
                Attributes = MemberAttributes.Public,
                Name = enumName
            };

            if (schema.EnumNames == null || ((JArray)schema.Enum).Count != schema.EnumNames.Length)
            {
                throw new InvalidOperationException("Enum names must be defined for each integer enum");
            }

            foreach (var index in Enumerable.Range(0, schema.EnumNames.Length))
            {
                var value = (int)(long)((JArray)schema.Enum)[index];
                enumType.Members.Add(new CodeMemberField(enumName, schema.EnumNames[index])
                {
                    InitExpression = new CodePrimitiveExpression(value)
                });
            }

            return enumType;
        }

        public static CodeFieldReferenceExpression GetEnumField(CodeTypeDeclaration enumType, int value)
        {
            var defaultMember = enumType.Members.Cast<CodeMemberField>().FirstOrDefault(m => (int)((CodePrimitiveExpression)m.InitExpression).Value == value);

            if (defaultMember == null)
            {
                throw new InvalidDataException("The default value is not in the enum list");
            }

            return new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(enumType.Name), defaultMember.Name);
        }
    }
}
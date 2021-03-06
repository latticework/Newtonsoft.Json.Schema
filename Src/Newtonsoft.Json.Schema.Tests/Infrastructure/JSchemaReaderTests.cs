﻿#region License
// Copyright (c) Newtonsoft. All Rights Reserved.
// License: https://raw.github.com/JamesNK/Newtonsoft.Json.Schema/master/LICENSE.md
#endregion

#if NETFX_CORE
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using TestFixture = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestClassAttribute;
using Test = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.TestMethodAttribute;
#elif ASPNETCORE50
using Xunit;
using Test = Xunit.FactAttribute;
using Assert = Newtonsoft.Json.Tests.XUnitAssert;
#else
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Utilities;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema.Infrastructure;
using NUnit.Framework;

namespace Newtonsoft.Json.Schema.Tests.Infrastructure
{
    [TestFixture]
    public class JSchemaReaderTests : TestFixtureBase
    {
        [Test]
        public void ReadEscapedReference()
        {
            JSchema schema = JSchema.Parse(@"{
  ""id"": ""http://www.jnk.com/"",
  ""properties"": {
    ""pattern_parent"": {
      ""patternProperties"": {
        ""///~~~test~/~/~"": {
          ""type"": ""object""
        }
      }
    },
    ""ref_parent"": {
      ""items"": {
        ""$ref"": ""#/properties/pattern_parent/patternProperties/~1~1~1~0~0~0test~0~1~0~1~0""
      }
    }
  }
}");

            Assert.AreEqual(2, schema.Properties.Count);

            JSchema nested = schema.Properties["pattern_parent"].PatternProperties["///~~~test~/~/~"];

            Assert.AreEqual(JSchemaType.Object, nested.Type);

            Assert.AreEqual(nested, schema.Properties["ref_parent"].Items[0]);
        }

        public static JSchema OpenSchemaFile(string name, JSchemaResolver resolver, Uri baseUri = null)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            string path = Path.Combine(baseDirectory, name);

            using (JsonReader reader = new JsonTextReader(new StreamReader(path)))
            {
                JSchema schema = JSchema.Load(reader, new JSchemaReaderSettings
                {
                    BaseUri = baseUri ?? new Uri(path, UriKind.RelativeOrAbsolute),
                    Resolver = resolver
                });

                return schema;
            }
        }

        [Test]
        public void FailToMatchPreloadedUriWithFragment()
        {
            try
            {
                JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();
                resolver.Add(new Uri("http://json-schema.org/geojson/crs.json#"), TestHelpers.OpenFile(@"resources\schemas\geojson\crs.json"));
                resolver.Add(new Uri("http://json-schema.org/geojson/bbox.json#"), TestHelpers.OpenFile(@"resources\schemas\geojson\bbox.json"));
                resolver.Add(new Uri("http://json-schema.org/geojson/geometry.json#"), TestHelpers.OpenFile(@"resources\schemas\geojson\geometry.json"));

                OpenSchemaFile(@"resources\schemas\geojson\geojson.json", resolver);
            }
            catch (JSchemaReaderException ex)
            {
                Assert.AreEqual("Could not resolve schema reference 'http://json-schema.org/geojson/crs.json#'. Path 'properties.crs', line 9, position 17.", ex.Message);

                Uri baseUri = new Uri(TestHelpers.ResolveFilePath(@"resources\schemas\geojson\geojson.json"));

                Assert.AreEqual(baseUri, ex.BaseUri);
            }
        }

        [Test]
        public void GeoJson()
        {
            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();

            resolver.Add(new Uri("http://json-schema.org/geojson/crs.json"), TestHelpers.OpenFile(@"resources\schemas\geojson\crs.json"));
            resolver.Add(new Uri("http://json-schema.org/geojson/bbox.json"), TestHelpers.OpenFile(@"resources\schemas\geojson\bbox.json"));
            resolver.Add(new Uri("http://json-schema.org/geojson/geometry.json"), TestHelpers.OpenFile(@"resources\schemas\geojson\geometry.json"));

            JSchema schema = OpenSchemaFile(@"resources\schemas\geojson\geojson.json", resolver, new Uri("http://json-schema.org/geojson/geojson.json"));

            JObject o = JObject.Parse(@"{
              ""type"": ""Feature"",
              ""geometry"": {
                ""type"": ""Point"",
                ""coordinates"": [125.6, 10.1]
              },
              ""properties"": {
                ""name"": ""Dinagat Islands""
              }
            }");

            bool isValid1 = o.IsValid(schema);
            Assert.IsTrue(isValid1);

            JObject o2 = JObject.Parse(@"{
              ""type"": ""Feature"",
              ""geometry"": {
                ""type"": ""Point"",
                ""coordinates"": [true, 10.1]
              },
              ""bbox"": [1, 2.1, ""string?!""],
              ""properties"": {
                ""name"": ""Dinagat Islands""
              }
            }");

            IList<ValidationError> errors;
            bool isValid2 = o2.IsValid(schema, out errors);
            Assert.IsFalse(isValid2);

            Assert.AreEqual(new Uri("http://json-schema.org/geojson/bbox.json"), errors[0].SchemaBaseUri);
            Assert.AreEqual(new Uri("http://json-schema.org/geojson/geojson.json"), errors[1].SchemaBaseUri);

            PrintErrorsRecursive(errors, 0);
        }

        private void PrintErrorsRecursive(IList<ValidationError> errors, int depth)
        {
            foreach (ValidationError validationError in errors)
            {
                string prefix = new string(' ', depth);

                Console.WriteLine(prefix + validationError.BuildExtendedMessage() + " - " + validationError.SchemaId + " - " + validationError.SchemaBaseUri);

                PrintErrorsRecursive(validationError.ChildErrors, depth + 2);
            }
        }

        [Test]
        public void ResolveScopedRelativeId()
        {
            string json = @"{
  ""id"": ""MyExplicitId"",
  ""type"": ""object"",
  ""properties"": {
    ""Name"": {
      ""type"": [
        ""string"",
        ""null""
      ]
    },
    ""Child"": {
      ""id"": ""MyExplicitId-1"",
      ""type"": [
        ""object"",
        ""null""
      ],
      ""properties"": {
        ""Name"": {
          ""type"": [
            ""string"",
            ""null""
          ]
        },
        ""Child"": {
          ""$ref"": ""#""
        }
      },
      ""required"": [
        ""Name"",
        ""Child""
      ]
    }
  },
  ""required"": [
    ""Name"",
    ""Child""
  ]
}";

            JSchema schema = JSchema.Parse(json);

            JSchema child = schema.Properties["Child"];

            Assert.AreEqual(child, child.Properties["Child"]);
        }

        [Test]
        public void ChromeManifest()
        {
            string schemaJson = TestHelpers.OpenFileText(@"resources\schemas\chrome-manifest.json");
            JSchema chromeManifestSchema = JSchema.Parse(schemaJson);

            Assert.AreEqual("JSON schema for Google Chrome extension manifest files", chromeManifestSchema.Title);

            Console.WriteLine(chromeManifestSchema.ToString());
        }

        [Test]
        public void Swagger()
        {
            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();
            resolver.Add(new Uri("http://json-schema.org/draft-04/schema"), TestHelpers.OpenFileText(@"resources\schemas\schema-draft-v4.json"));

            string schemaJson = TestHelpers.OpenFileText(@"resources\schemas\swagger-2.0.json");
            JSchema swaggerSchema = JSchema.Parse(schemaJson, resolver);

            string json = TestHelpers.OpenFileText(@"resources\json\swagger-petstore.json");
            JObject o = JObject.Parse(json);

            IList<string> messages;
            bool valid = o.IsValid(swaggerSchema, out messages);

            Assert.IsFalse(valid);
            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual(@"String 'http://petstore.swagger.io' does not match regex pattern '^[^{}/ :\\]+(?::\d+)?$'. Path 'host', line 16, position 41.", messages[0]);

            Console.WriteLine(swaggerSchema.ToString());
        }

        [Test]
        public void BadRegexPattern()
        {
            string schema = @"{
               ""pattern"": ""^[01][0-9]:[\\d$""
            }";

            JSchema s = JSchema.Parse(schema);

            IList<ValidationError> errors;
            new JValue("j").IsValid(s, out errors);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(@"Could not validate string with regex pattern '^[01][0-9]:[\d$'. There was an error parsing the regex: parsing ""^[01][0-9]:[\d$"" - Unterminated [] set.", errors[0].Message);
        }

        [Test]
        public void ReadAllResourceSchemas()
        {
            string schemaDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"resources\schemas");

            string[] schemaFilePaths = Directory.GetFiles(schemaDir, "*.json");

            foreach (string schemaFilePath in schemaFilePaths)
            {
                try
                {
                    using (StreamReader sr = File.OpenText(schemaFilePath))
                    using (JsonTextReader reader = new JsonTextReader(sr))
                    {
                        JSchema schema = JSchema.Load(reader, new JSchemaUrlResolver());
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format("Error reading schema from '{0}'.", schemaFilePath), ex);
                }
            }
        }

        [Test]
        public void DuplicateProperties()
        {
            string json = @"
{
  ""description"": ""A person"",
  ""type"": ""object"",
  ""properties"":
  {
    ""name"": {""type"":""string""},
    ""hobbies"": {
      ""type"": ""array"",
      ""items"": {""type"":""string""}
    },
    ""name"": {""type"":[""string"",""null""]}
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("A person", schema.Description);
            Assert.AreEqual(JSchemaType.Object, schema.Type);

            Assert.AreEqual(2, schema.Properties.Count);

            Assert.AreEqual(JSchemaType.String | JSchemaType.Null, schema.Properties["name"].Type);
            Assert.AreEqual(JSchemaType.Array, schema.Properties["hobbies"].Type);
            Assert.AreEqual(JSchemaType.String, schema.Properties["hobbies"].Items[0].Type);
        }

        [Test]
        public void Simple()
        {
            string json = @"
{
  ""description"": ""A person"",
  ""type"": ""object"",
  ""properties"":
  {
    ""name"": {""type"":""string""},
    ""hobbies"": {
      ""type"": ""array"",
      ""items"": {""type"":""string""}
    }
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("A person", schema.Description);
            Assert.AreEqual(JSchemaType.Object, schema.Type);

            Assert.AreEqual(2, schema.Properties.Count);

            Assert.AreEqual(JSchemaType.String, schema.Properties["name"].Type);
            Assert.AreEqual(JSchemaType.Array, schema.Properties["hobbies"].Type);
            Assert.AreEqual(JSchemaType.String, schema.Properties["hobbies"].Items[0].Type);
        }

        [Test]
        public void MultipleTypes()
        {
            string json = @"{
  ""description"":""Age"",
  ""type"":[""string"", ""integer""]
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Age", schema.Description);
            Assert.AreEqual(JSchemaType.String | JSchemaType.Integer, schema.Type);
        }

        [Test]
        public void MultipleItems()
        {
            string json = @"{
  ""description"":""MultipleItems"",
  ""type"":""array"",
  ""items"": [{""type"":""string""},{""type"":""array""}]
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("MultipleItems", schema.Description);
            Assert.AreEqual(JSchemaType.String, schema.Items[0].Type);
            Assert.AreEqual(JSchemaType.Array, schema.Items[1].Type);
        }

        [Test]
        public void Extends()
        {
            string json = @"{
  ""extends"": [{""type"":""string""},{""type"":""null""}],
  ""description"":""Extends""
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Extends", schema.Description);
            Assert.AreEqual(JSchemaType.String, schema.AllOf[0].Type);
            Assert.AreEqual(JSchemaType.Null, schema.AllOf[1].Type);
        }

        [Test]
        public void AdditionalProperties()
        {
            string json = @"{
  ""description"":""AdditionalProperties"",
  ""type"":[""string"", ""integer""],
  ""additionalProperties"":{""type"":[""object"", ""boolean""]}
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("AdditionalProperties", schema.Description);
            Assert.AreEqual(JSchemaType.Object | JSchemaType.Boolean, schema.AdditionalProperties.Type);
        }

        [Test]
        public void Required()
        {
            string json = @"{
  ""description"":""Required"",
  ""required"":true
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Required", schema.Description);
            Assert.AreEqual(true, schema.DeprecatedRequired);
        }

        [Test]
        public void DeprecatedRequired()
        {
            string schemaJson = @"{
  ""description"":""A person"",
  ""type"":""object"",
  ""properties"":
  {
    ""name"":{""type"":""string""},
    ""hobbies"":{""type"":""string"",""required"":true},
    ""age"":{""type"":""integer"",""required"":true}
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(schemaJson)));

            Assert.AreEqual(2, schema.Required.Count);
            Assert.AreEqual("hobbies", schema.Required[0]);
            Assert.AreEqual("age", schema.Required[1]);
        }

        [Test]
        public void ExclusiveMinimum_ExclusiveMaximum()
        {
            string json = @"{
  ""exclusiveMinimum"":true,
  ""exclusiveMaximum"":true
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(true, schema.ExclusiveMinimum);
            Assert.AreEqual(true, schema.ExclusiveMaximum);
        }

        [Test]
        public void Id()
        {
            string json = @"{
  ""description"":""Id"",
  ""id"":""testid""
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Id", schema.Description);
            Assert.AreEqual(new Uri("testid", UriKind.RelativeOrAbsolute), schema.Id);
        }

        [Test]
        public void Title()
        {
            string json = @"{
  ""description"":""Title"",
  ""title"":""testtitle""
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Title", schema.Description);
            Assert.AreEqual("testtitle", schema.Title);
        }

        [Test]
        public void Pattern()
        {
            string json = @"{
  ""description"":""Pattern"",
  ""pattern"":""testpattern""
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Pattern", schema.Description);
            Assert.AreEqual("testpattern", schema.Pattern);
        }

        [Test]
        public void Dependencies()
        {
            string json = @"{
            ""dependencies"": {""bar"": ""foo""}
        }";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("foo", ((IList<string>)schema.Dependencies["bar"])[0]);
        }

        [Test]
        public void Dependencies_SchemaDependency()
        {
            string json = @"{
  ""dependencies"": {
    ""bar"": ""foo"",
    ""foo"": { ""title"": ""Dependency schema"" },
    ""stuff"": [""blah"",""blah2""]
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("foo", ((IList<string>) schema.Dependencies["bar"])[0]);
            Assert.AreEqual("Dependency schema", ((JSchema)schema.Dependencies["foo"]).Title);
            Assert.AreEqual("blah", ((IList<string>)schema.Dependencies["stuff"])[0]);
            Assert.AreEqual("blah2", ((IList<string>)schema.Dependencies["stuff"])[1]);
        }

        [Test]
        public void MinimumMaximum()
        {
            string json = @"{
  ""description"":""MinimumMaximum"",
  ""minimum"":1.1,
  ""maximum"":1.2,
  ""minItems"":1,
  ""maxItems"":2,
  ""minLength"":5,
  ""maxLength"":50,
  ""divisibleBy"":3,
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("MinimumMaximum", schema.Description);
            Assert.AreEqual(1.1, schema.Minimum);
            Assert.AreEqual(1.2, schema.Maximum);
            Assert.AreEqual(1, schema.MinimumItems);
            Assert.AreEqual(2, schema.MaximumItems);
            Assert.AreEqual(5, schema.MinimumLength);
            Assert.AreEqual(50, schema.MaximumLength);
            Assert.AreEqual(3, schema.MultipleOf);
        }

        [Test]
        public void DisallowSingleType()
        {
            string json = @"{
          ""description"":""DisallowSingleType"",
          ""disallow"":""string""
        }";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("DisallowSingleType", schema.Description);
            Assert.AreEqual(JSchemaType.String, schema.Not.Type);
        }

        [Test]
        public void DisallowMultipleTypes()
        {
            string json = @"{
          ""description"":""DisallowMultipleTypes"",
          ""disallow"":[""string"",""number""]
        }";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("DisallowMultipleTypes", schema.Description);
            Assert.AreEqual(JSchemaType.String | JSchemaType.Number, schema.Not.Type);
        }

        [Test]
        public void Enum()
        {
            string json = @"{
  ""description"":""Type"",
  ""type"":[""string"",""array""],
  ""enum"":[""string"",""object"",""array"",""boolean"",""number"",""integer"",""null"",""any""]
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("Type", schema.Description);
            Assert.AreEqual(JSchemaType.String | JSchemaType.Array, schema.Type);

            Assert.AreEqual(8, schema.Enum.Count);
            Assert.AreEqual("string", (string)schema.Enum[0]);
            Assert.AreEqual("any", (string)schema.Enum[schema.Enum.Count - 1]);
        }

        [Test]
        public void CircularReference()
        {
            string json = @"{
  ""id"":""CircularReferenceArray"",
  ""description"":""CircularReference"",
  ""type"":[""array""],
  ""items"":{""$ref"":""CircularReferenceArray""}
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual("CircularReference", schema.Description);
            Assert.AreEqual(new Uri("CircularReferenceArray", UriKind.RelativeOrAbsolute), schema.Id);
            Assert.AreEqual(JSchemaType.Array, schema.Type);

            Assert.AreEqual(schema, schema.Items[0]);
        }

        [Test]
        public void ReferenceToNestedSchemaWithIdInResolvedSchema()
        {
            JSchema nested = new JSchema();
            nested.Id = new Uri("nested.json", UriKind.RelativeOrAbsolute);
            nested.Type = JSchemaType.Integer;

            JSchema root = new JSchema
            {
                Id = new Uri("http://test.test"),
                Items =
                {
                    nested
                }
            };
            string rootJson = root.ToString();

            string json = @"{
  ""type"":[""array""],
  ""items"":{""$ref"":""http://test.test/nested.json""}
}";

            NestedPreloadedResolver resolver = new NestedPreloadedResolver();
            resolver.Add(root.Id, rootJson);

            JSchemaReader schemaReader = new JSchemaReader(resolver);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(new Uri("nested.json", UriKind.RelativeOrAbsolute), schema.Items[0].Id);

            Assert.AreEqual(JSchemaType.Integer, schema.Items[0].Type);
        }

        [Test]
        [Ignore]
        public void ReferenceToNestedSchemaWithIdInResolvedSchema_ExtensionData()
        {
            JSchema nested = new JSchema();
            nested.Id = new Uri("nested.json", UriKind.RelativeOrAbsolute);

            JSchema root = new JSchema
            {
                Id = new Uri("http://test.test"),
                ExtensionData =
                {
                    { "nested", nested }
                }
            };
            string rootJson = root.ToString();

            string json = @"{
  ""type"":[""array""],
  ""items"":{""$ref"":""http://test.test/nested.json""}
}";

            NestedPreloadedResolver resolver = new NestedPreloadedResolver();
            resolver.Add(root.Id, rootJson);

            JSchemaReader schemaReader = new JSchemaReader(resolver);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(new Uri("nested.json", UriKind.RelativeOrAbsolute), schema.Items[0].Id);

            Assert.AreEqual(nested, schema.Items[0]);
        }

        [Test]
        public void ReferenceToNestedSchemaWithIdInResolvedSchema_Root()
        {
            JSchema nested = new JSchema();
            nested.Id = new Uri("nested.json", UriKind.RelativeOrAbsolute);
            nested.Type = JSchemaType.String;

            JSchema root = new JSchema
            {
                Id = new Uri("http://test.test"),
                Items = 
                {
                    nested
                }
            };

            string json = @"{""$ref"":""http://test.test/nested.json""}";

            NestedPreloadedResolver resolver = new NestedPreloadedResolver();
            resolver.Add(root.Id, root.ToString());

            JSchemaReader schemaReader = new JSchemaReader(resolver);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(new Uri("nested.json", UriKind.RelativeOrAbsolute), schema.Id);

            Assert.AreEqual(JSchemaType.String, schema.Type);
        }

        [Test]
        public void UnresolvedReference()
        {
            try
            {
                string json = @"{
  ""id"":""CircularReferenceArray"",
  ""description"":""CircularReference"",
  ""type"":[""array""],
  ""items"":{""$ref"":""#/definitions/nested""},
  ""definitions"":{
    ""nested"": {""$ref"":""MyUnresolvedReference""}
  }
}";

                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }
            catch (JSchemaReaderException ex)
            {
                Assert.AreEqual(@"Could not resolve schema reference 'MyUnresolvedReference'. Path 'definitions.nested', line 7, position 16.", ex.Message);

                Assert.AreEqual(null, ex.BaseUri);
            }
        }

        [Test]
        public void PatternProperties()
        {
            string json = @"{
  ""patternProperties"": {
    ""[abc]"": { ""id"":""Blah"" }
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.IsNotNull(schema.PatternProperties);
            Assert.AreEqual(1, schema.PatternProperties.Count);
            Assert.AreEqual(new Uri("Blah", UriKind.RelativeOrAbsolute), schema.PatternProperties["[abc]"].Id);
        }

        [Test]
        public void AdditionalItems()
        {
            string json = @"{
    ""items"": [],
    ""additionalItems"": {""type"": ""integer""}
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.IsNotNull(schema.AdditionalItems);
            Assert.AreEqual(JSchemaType.Integer, schema.AdditionalItems.Type);
            Assert.AreEqual(true, schema.AllowAdditionalItems);
        }

        [Test]
        public void DisallowAdditionalItems()
        {
            string json = @"{
    ""items"": [],
    ""additionalItems"": false
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.IsNull(schema.AdditionalItems);
            Assert.AreEqual(false, schema.AllowAdditionalItems);
        }

        [Test]
        public void AllowAdditionalItems()
        {
            string json = @"{
    ""items"": {},
    ""additionalItems"": false
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.IsNull(schema.AdditionalItems);
            Assert.AreEqual(false, schema.AllowAdditionalItems);
        }

        [Test]
        public void Reference_BackwardsLocation()
        {
            string json = @"{
  ""properties"": {
    ""foo"": {""type"": ""integer""},
    ""bar"": {""$ref"": ""#/properties/foo""}
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(schema.Properties["foo"], schema.Properties["bar"]);
        }

        [Test]
        public void Reference_ForwardsLocation()
        {
            string json = @"{
  ""properties"": {
    ""bar"": {""$ref"": ""#/properties/foo""},
    ""foo"": {""type"": ""integer""}
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(schema.Properties["foo"], schema.Properties["bar"]);
        }

        [Test]
        public void Reference_NonStandardLocation()
        {
            string json = @"{
  ""properties"": {
    ""foo"": {""$ref"": ""#/common/foo""},
    ""foo2"": {""$ref"": ""#/common/foo""},
    ""bar"": {""$ref"": ""#/common/foo/bar""}
  },
  ""common"": {
    ""foo"": {
      ""type"": ""integer"",
      ""bar"": {
        ""type"": ""object""
      }
    }
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual((JSchema)schema.ExtensionData["common"]["foo"], schema.Properties["foo"]);
            Assert.AreEqual((JSchema)schema.ExtensionData["common"]["foo"], schema.Properties["foo2"]);
            Assert.AreEqual((JSchema)schema.ExtensionData["common"]["foo"]["bar"], schema.Properties["bar"]);
        }

        [Test]
        public void EscapedReferences()
        {
            string json = @"{
  ""tilda~field"": {""type"": ""integer""},
  ""slash/field"": {""type"": ""object""},
  ""percent%field"": {""type"": ""array""},
  ""properties"": {
    ""tilda"": {""$ref"": ""#/tilda~0field""},
    ""slash"": {""$ref"": ""#/slash~1field""},
    ""percent"": {""$ref"": ""#/percent%25field""}
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(JSchemaType.Integer, schema.Properties["tilda"].Type);
            Assert.AreEqual(JSchemaType.Object, schema.Properties["slash"].Type);
            Assert.AreEqual(JSchemaType.Array, schema.Properties["percent"].Type);
        }

        [Test]
        public void References_Array()
        {
            string json = @"{
            ""array"": [{""type"": ""integer""},{""prop"":{""type"": ""object""}}],
            ""items"": [{""type"": ""string""}],
            ""properties"": {
                ""array"": {""$ref"": ""#/array/0""},
                ""arrayprop"": {""$ref"": ""#/array/1/prop""},
                ""items"": {""$ref"": ""#/items/0""}
            }
        }";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.AreEqual(JSchemaType.Integer, schema.Properties["array"].Type);
            Assert.AreEqual(JSchemaType.Object, schema.Properties["arrayprop"].Type);
            Assert.AreEqual(JSchemaType.String, schema.Properties["items"].Type);
        }

        [Test]
        public void References_IndexTooBig()
        {
            // JsonException : Could not resolve schema reference '#/array/10'.

            string json = @"{
            ""array"": [{""type"": ""integer""},{""prop"":{""type"": ""object""}}],
            ""properties"": {
                ""array"": {""$ref"": ""#/array/0""},
                ""arrayprop"": {""$ref"": ""#/array/10""}
            }
        }";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }, "Could not resolve schema reference '#/array/10'. Path 'properties.arrayprop', line 5, position 31.");
        }

        [Test]
        public void References_IndexNegative()
        {
            string json = @"{
            ""array"": [{""type"": ""integer""},{""prop"":{""type"": ""object""}}],
            ""properties"": {
                ""array"": {""$ref"": ""#/array/0""},
                ""arrayprop"": {""$ref"": ""#/array/-1""}
            }
        }";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }, "Could not resolve schema reference '#/array/-1'. Path 'properties.arrayprop', line 5, position 31.");
        }

        [Test]
        public void References_IndexNotInteger()
        {
            string json = @"{
            ""array"": [{""type"": ""integer""},{""prop"":{""type"": ""object""}}],
            ""properties"": {
                ""array"": {""$ref"": ""#/array/0""},
                ""arrayprop"": {""$ref"": ""#/array/one""}
            }
        }";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }, "Could not resolve schema reference '#/array/one'. Path 'properties.arrayprop', line 5, position 31.");
        }

        [Test]
        public void References_Items_IndexNotInteger()
        {
            string json = @"{
            ""items"": [{""type"": ""integer""},{""prop"":{""type"": ""object""}}],
            ""properties"": {
                ""array"": {""$ref"": ""#/items/0""},
                ""arrayprop"": {""$ref"": ""#/items/one""}
            }
        }";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }, "Could not resolve schema reference '#/items/one'. Path 'properties.arrayprop', line 5, position 31.");
        }

        [Test]
        public void Reference_InnerSchemaOfExternalSchema_Failure()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                TestHelpers.OpenSchemaFile(@"resources\schemas\grunt-clean-task.json");
            }, "Could not resolve schema reference 'http://json.schemastore.org/grunt-task#/definitions/fileFormat'. Path 'additionalProperties.anyOf[0]', line 34, position 5.");
        }

        [Test]
        public void Reference_InnerSchemaOfExternalSchema()
        {
            JSchemaUrlResolver resolver = new JSchemaUrlResolver();

            JSchema cleanSchema = TestHelpers.OpenSchemaFile(@"resources\schemas\grunt-clean-task.json", resolver);

            JSchema fileFormatSchema = cleanSchema.AdditionalProperties.AnyOf[0];

            Assert.NotNull(fileFormatSchema.BaseUri);
            Assert.AreEqual(true, fileFormatSchema.Properties.ContainsKey("files"));
        }

        [Test]
        public void ResolveRelativeFilePaths()
        {
            JSchemaUrlResolver resolver = new JSchemaUrlResolver();

            JSchema rootSchema = TestHelpers.OpenSchemaFile(@"resources\schemas\custom\root.json", resolver);

            Assert.AreEqual("Root", rootSchema.Title);
            Assert.IsTrue(rootSchema.BaseUri.OriginalString.EndsWith("root.json"));

            JSchema sub1 = rootSchema.Properties["property1"];
            Assert.AreEqual("Sub1", sub1.Title);
            Assert.IsTrue(sub1.BaseUri.OriginalString.EndsWith("sub1.json"));

            JSchema sub2 = rootSchema.Properties["property2"];
            Assert.AreEqual("Sub2", sub2.Title);
            Assert.IsTrue(sub2.BaseUri.OriginalString.EndsWith("sub/sub2.json"));

            JSchema nestedSub3 = sub2.Properties["property1"];

            JSchema sub2Def1 = rootSchema.Properties["property3"];
            Assert.AreEqual("Def1", sub2Def1.Title);
            Assert.IsTrue(sub2Def1.BaseUri.OriginalString.EndsWith("sub/sub2.json"));

            JSchema sub3 = rootSchema.Properties["property4"];
            Assert.AreEqual("Sub3", sub3.Title);
            Assert.IsTrue(sub3.BaseUri.OriginalString.EndsWith("sub3.json"));

            Assert.AreEqual(nestedSub3, sub3);
        }

        [Test]
        public void ResolveCircularExternalReferences()
        {
            string path = TestHelpers.ResolveFilePath(@"resources\schemas\custom\obj_branch.schema.json");

            string schemaJson = File.ReadAllText(path);
            JSchema schema = JSchema.Parse(schemaJson, new JSchemaReaderSettings
            {
                BaseUri = new Uri(path),
                Resolver = new JSchemaUrlResolver()
            });

            Assert.IsTrue(schema.BaseUri.OriginalString.EndsWith("obj_branch.schema.json"));

            JSchema propSeeAlsoSchema = schema.Properties["see_also"].AllOf[0];

            Assert.IsTrue(propSeeAlsoSchema.BaseUri.OriginalString.EndsWith("prop_see_also.schema.json"));

            JSchema objBranch = propSeeAlsoSchema.Items[0].AllOf[0];

            Assert.AreEqual(schema, objBranch);
        }

        [Test]
        public void ResolveRelativeFilePaths_InvalidNestedRef()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchemaUrlResolver resolver = new JSchemaUrlResolver();

                    TestHelpers.OpenSchemaFile(@"resources\schemas\custom\root_invalidnestedref.json", resolver);
                },
                "Could not resolve schema reference 'sub/sub2.json#/definitions/invalid'. Path 'properties.property2', line 7, position 23.");
        }

        [Test]
        public void Reference_UnusedInnerSchemaOfExternalSchema()
        {
            string schemaJson = @"{
  ""definitions"": {
    ""unused"": {
      ""not"": {
        ""$ref"": ""#/definitions/used_by_unused""
      }
    },
    ""used_by_unused"": {
      ""title"": ""used by unused""
    }
  }
}";

            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();
            resolver.Add(new Uri("http://localhost/base"), schemaJson);

            string json = @"{
  ""not"": {
    ""$ref"": ""http://localhost/base#/definitions/unused""
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(resolver);
            JSchema refSchema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            Assert.IsNotNull(refSchema.Not);
            Assert.AreEqual("used by unused", refSchema.Not.Not.Title);
        }

        [Test]
        public void ErrorInExternalSchema()
        {
            string schemaJson = @"{
  ""definitions"": {
    ""unused"": {
      ""not"": {
        ""$ref"": ""#/definitions/used_by_unused""
      }
    },
    ""used_by_unused"": {
      ""$ref"": ""#/definitions/invalid""
    }
  }
}";

            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();
            resolver.Add(new Uri("http://localhost/base"), schemaJson);

            string json = @"{
  ""not"": {
    ""$ref"": ""http://localhost/base#/definitions/unused""
  }
}";

            try
            {
                JSchemaReader schemaReader = new JSchemaReader(resolver);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }
            catch (JSchemaReaderException ex)
            {
                Assert.AreEqual("Error when resolving schema reference 'http://localhost/base#/definitions/unused'. Path 'not', line 2, position 11.", ex.Message);
                Assert.AreEqual(null, ex.BaseUri);

                JSchemaReaderException inner = (JSchemaReaderException)ex.InnerException;
                Assert.AreEqual("Could not resolve schema reference '#/definitions/invalid'. Path 'definitions.used_by_unused', line 8, position 24.", inner.Message);
                Assert.AreEqual(new Uri("http://localhost/base"), inner.BaseUri);
            }
        }

        [Test]
        public void Extends_Multiple()
        {
            string json = @"{
  ""type"":""object"",
  ""extends"":{""type"":""string""},
  ""additionalProperties"":{""type"":""string""}
}";

            JSchema s = JSchema.Parse(json);

            StringWriter writer = new StringWriter();
            JsonTextWriter jsonWriter = new JsonTextWriter(writer);
            jsonWriter.Formatting = Formatting.Indented;

            string newJson = s.ToString();

            StringAssert.AreEqual(@"{
  ""type"": ""object"",
  ""additionalProperties"": {
    ""type"": ""string""
  },
  ""allOf"": [
    {
      ""type"": ""string""
    }
  ]
}", newJson);

            json = @"{
  ""type"":""object"",
  ""extends"":[{""type"":""string""}],
  ""additionalProperties"":{""type"":""string""}
}";

            s = JSchema.Parse(json);

            writer = new StringWriter();
            jsonWriter = new JsonTextWriter(writer);
            jsonWriter.Formatting = Formatting.Indented;

            newJson = s.ToString();

            StringAssert.AreEqual(@"{
  ""type"": ""object"",
  ""additionalProperties"": {
    ""type"": ""string""
  },
  ""allOf"": [
    {
      ""type"": ""string""
    }
  ]
}", newJson);


            json = @"{
  ""type"":""object"",
  ""extends"":[{""type"":""string""},{""type"":""object""}],
  ""additionalProperties"":{""type"":""string""}
}";

            s = JSchema.Parse(json);

            writer = new StringWriter();
            jsonWriter = new JsonTextWriter(writer);
            jsonWriter.Formatting = Formatting.Indented;

            newJson = s.ToString();

            StringAssert.AreEqual(@"{
  ""type"": ""object"",
  ""additionalProperties"": {
    ""type"": ""string""
  },
  ""allOf"": [
    {
      ""type"": ""string""
    },
    {
      ""type"": ""object""
    }
  ]
}", newJson);
        }

        [Test]
        public void SchemaInDisallow()
        {
            JSchema schema = JSchema.Parse(@"{
	          ""$schema"": ""http://json-schema.org/draft-03/schema#"",
              ""disallow"": [
                ""string"",
                {
                  ""type"": ""object"",
                  ""properties"": {
                    ""foo"": {
                      ""type"": ""string""
                    }
                  }
                }
              ]
            }");

            Assert.IsNotNull(schema.Not);
            Assert.AreEqual(2, schema.Not.AnyOf.Count);
            Assert.AreEqual(JSchemaType.String, schema.Not.AnyOf[0].Type);
            Assert.AreEqual(JSchemaType.Object, schema.Not.AnyOf[1].Type);
            Assert.AreEqual(1, schema.Not.AnyOf[1].Properties.Count);
        }

        [Test]
        public void Any_Draft4_ValidateVersion()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReaderSettings settings = new JSchemaReaderSettings
                {
                    ValidateVersion = true
                };
                JSchema.Parse(@"{
	              ""$schema"": ""http://json-schema.org/draft-04/schema#"",
                  ""type"": ""any""
                }", settings);
            }, "Validation error raised by version schema 'http://json-schema.org/draft-04/schema#': JSON does not match any schemas from 'anyOf'. Path 'type', line 3, position 32.");
        }

        [Test]
        public void Any_Draft4_ValidateVersion_Nested()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReaderSettings settings = new JSchemaReaderSettings
                {
                    ValidateVersion = true
                };

                JSchema.Parse(@"{
  ""$schema"": ""http://json-schema.org/draft-04/schema#"",
  ""properties"": {
    ""test"": {
      ""$ref"": ""#/definitions/hasAny""
    }
  },
  ""definitions"": {
    ""hasAny"": {
      ""type"": ""any""
    }
  }
}", settings);

            }, "Validation error raised by version schema 'http://json-schema.org/draft-04/schema#': JSON does not match any schemas from 'anyOf'. Path 'definitions.hasAny.type', line 10, position 20.");
        }

        [Test]
        public void Any_Draft4()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchema.Parse(@"{
  ""$schema"": ""http://json-schema.org/draft-04/schema#"",
  ""type"": ""any""
}");
                }, "Invalid JSON schema type: any. Path 'type', line 3, position 16.");
        }

        [Test]
        public void Required_Draft4()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchema.Parse(@"{
  ""$schema"": ""http://json-schema.org/draft-04/schema#"",
  ""required"": true
}");
                },
                "Unexpected token encountered when reading value for 'required'. Expected StartArray, got Boolean. Path 'required', line 3, position 19.");
        }

        [Test]
        public void Required_Draft3()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchema.Parse(@"{
  ""$schema"": ""http://json-schema.org/draft-03/schema#"",
  ""required"": []
}");
                },
                "Unexpected token encountered when reading value for 'required'. Expected Boolean, got StartArray. Path 'required', line 3, position 16.");
        }

        [Test]
        public void Keywords_Draft4()
        {
            JSchema schema = JSchema.Parse(@"{
  ""$schema"": ""http://json-schema.org/draft-04/schema#"",
  ""disallow"": {},
  ""divisibleBy"": 9
}");

            Assert.IsTrue(JToken.DeepEquals(new JObject(), schema.ExtensionData["disallow"]));
            Assert.IsTrue(JToken.DeepEquals(9, schema.ExtensionData["divisibleBy"]));
        }

        [Test]
        public void Keywords_Draft3()
        {
            JSchema schema = JSchema.Parse(@"{
  ""$schema"": ""http://json-schema.org/draft-03/schema#"",
  ""not"": {},
  ""allOf"": [],
  ""anyOf"": [],
  ""oneOf"": [],
  ""multipleOf"": 9
}");

            Assert.IsTrue(JToken.DeepEquals(new JObject(), schema.ExtensionData["not"]));
            Assert.IsTrue(JToken.DeepEquals(new JArray(), schema.ExtensionData["allOf"]));
            Assert.IsTrue(JToken.DeepEquals(new JArray(), schema.ExtensionData["anyOf"]));
            Assert.IsTrue(JToken.DeepEquals(new JArray(), schema.ExtensionData["oneOf"]));
            Assert.IsTrue(JToken.DeepEquals(9, schema.ExtensionData["multipleOf"]));
        }

        [Test]
        public void ReadAny()
        {
            JSchema schema = JSchema.Parse(@"{
                ""type"": ""any""
            }");

            Assert.IsNull(schema.Type);
            Assert.AreEqual(0, schema.AnyOf.Count);

            schema = JSchema.Parse(@"{
                ""type"": [""any"", ""string""]
            }");

            Assert.IsNull(schema.Type);
            Assert.AreEqual(0, schema.AnyOf.Count);

            schema = JSchema.Parse(@"{
                ""type"": [""integer"", ""any"", ""string""]
            }");

            Assert.IsNull(schema.Type);
            Assert.AreEqual(0, schema.AnyOf.Count);

            schema = JSchema.Parse(@"{
                ""type"": [""integer"", ""any"", {}]
            }");

            Assert.IsNull(schema.Type);
            Assert.AreEqual(0, schema.AnyOf.Count);
        }

        [Test]
        public void ReadSchema()
        {
            string json = @"{
  ""id"": ""root"",
  ""properties"": {
    ""storage"": {
      ""$ref"": ""#/definitions/file""
    }
  },
  ""items"": [
    {
      ""type"": [
        ""integer"",
        ""null""
      ]
    },
    {
      ""$ref"": ""#/definitions/file""
    }
  ],
  ""allOf"": [
    {
      ""type"": [
        ""integer"",
        ""null""
      ]
    },
    {
      ""$ref"": ""#/definitions/file""
    }
  ],
  ""oneOf"": [
    {
      ""type"": [
        ""null""
      ]
    }
  ],
  ""anyOf"": [
    {
      ""type"": [
        ""string""
      ]
    }
  ],
  ""not"": {
  },
  ""definitions"": {
    ""file"": {
      ""id"": ""file"",
      ""properties"": {
        ""blah"": {
          ""$ref"": ""#""
        },
        ""blah2"": {
          ""$ref"": ""root""
        },
        ""blah3"": {
          ""$ref"": ""#/definitions/parent""
        }
      },
      ""definitions"": {
        ""parent"": {
          ""$ref"": ""#""
        }
      }
    }
  }
}";

            JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);

            JSchema schema = schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));

            JToken t1 = schema.ExtensionData["definitions"]["file"];
            JSchemaAnnotation a1 = t1.Annotation<JSchemaAnnotation>();

            Assert.AreEqual(new Uri("root", UriKind.RelativeOrAbsolute), schema.Id);
            Assert.AreEqual(1, schema.Properties.Count);
            Assert.AreEqual(a1.Schema, schema.Properties["storage"]);

            JSchema fileSchema = schema.Properties["storage"];

            Assert.AreEqual(fileSchema, fileSchema.Properties["blah"]);
            Assert.AreEqual(schema, fileSchema.Properties["blah2"]);
            Assert.AreEqual(fileSchema, fileSchema.Properties["blah3"]);

            Assert.AreEqual(2, schema.Items.Count);
            Assert.AreEqual(true, schema.ItemsPositionValidation);

            Assert.AreEqual(JSchemaType.Integer | JSchemaType.Null, schema.Items[0].Type);
            Assert.AreEqual(a1.Schema, schema.Items[1]);

            Assert.AreEqual(2, schema.AllOf.Count);
            Assert.AreEqual(JSchemaType.Integer | JSchemaType.Null, schema.AllOf[0].Type);
            Assert.AreEqual(a1.Schema, schema.AllOf[1]);

            Assert.AreEqual(1, schema.OneOf.Count);
            Assert.AreEqual(JSchemaType.Null, schema.OneOf[0].Type);

            Assert.AreEqual(1, schema.AnyOf.Count);
            Assert.AreEqual(JSchemaType.String, schema.AnyOf[0].Type);

            Assert.AreEqual(null, schema.Not.Type);
        }

        [Test]
        public void RefToExternalRef()
        {
            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();

            JSchema subSchema = JSchema.Parse(@"{
                ""integer"": {
                    ""type"": ""integer""
                }, 
                ""refToInteger"": {
                    ""$ref"": ""#/integer""
                }
            }");

            resolver.Add(new Uri("http://localhost:1234/subSchemas.json"), subSchema.ToString());

            JSchema schema = JSchema.Parse(@"{
                ""$ref"": ""http://localhost:1234/subSchemas.json#/refToInteger""
            }", resolver);

            Assert.AreEqual(JSchemaType.Integer, schema.Type);
        }

        [Test]
        public void NestedRef()
        {
            JSchema schema = JSchema.Parse(@"{
                ""definitions"": {
                    ""a"": {""type"": ""integer""},
                    ""b"": {""$ref"": ""#/definitions/a""},
                    ""c"": {""$ref"": ""#/definitions/b""}
                },
                ""$ref"": ""#/definitions/c""
            }");

            Assert.AreEqual(JSchemaType.Integer, schema.Type);

            StringAssert.AreEqual(@"{
  ""definitions"": {
    ""a"": {
      ""$ref"": ""#""
    },
    ""b"": {
      ""$ref"": ""#""
    },
    ""c"": {
      ""$ref"": ""#""
    }
  },
  ""type"": ""integer""
}", schema.ToString());
        }

        [Test]
        public void ChainedRef()
        {
            JSchema schema = JSchema.Parse(@"{
                ""definitions"": {
                    ""a"": {""type"": ""integer""},
                    ""b"": {""$ref"": ""#/definitions/a""},
                    ""c"": {""$ref"": ""#/definitions/b""}
                },
                ""properties"": {
                    ""id"": {""$ref"": ""#/definitions/c""}
                }
            }");

            Assert.AreEqual(JSchemaType.Integer, schema.Properties["id"].Type);

            Console.WriteLine(schema.ToString());
        }

        [Test]
        public void InvalidReference()
        {
            string json = @"{
  ""$ref"": ""#/missing""
}";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }, "Could not resolve schema reference '#/missing'. Path '', line 1, position 1.");
        }

        [Test]
        public void ReferenceSelf()
        {
            string json = @"{
  ""$ref"": ""#""
}";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                schemaReader.ReadRoot(new JsonTextReader(new StringReader(json)));
            }, "Could not resolve schema reference '#'. Path '', line 1, position 1.");
        }

        [Test]
        public void CircularRef()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchema.Parse(@"{
                    ""definitions"": {
                        ""a"": {""$ref"": ""#/definitions/c""},
                        ""b"": {""$ref"": ""#/definitions/a""},
                        ""c"": {""$ref"": ""#/definitions/b""}
                    },
                    ""properties"": {
                        ""id"": {""$ref"": ""#/definitions/c""}
                    }
                }");

            }, "Could not resolve schema reference '#/definitions/c'. Path 'properties.id', line 8, position 32.");
        }

        [Test]
        public void NestedCircularRef()
        {
            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchema.Parse(@"{
                  ""$ref"": ""#/a"",
                  ""a"": { ""$ref"": ""#/b"" },
                  ""b"": { ""$ref"": ""#/a"" }
                }");

            }, "Could not resolve schema reference '#/a'. Path '', line 1, position 1.");
        }

        [Test]
        public void Draft4()
        {
            JSchema schema = JSchema.Parse(@"{
    ""id"": ""http://json-schema.org/draft-04/schema#"",
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""description"": ""Core schema meta-schema"",
    ""definitions"": {
        ""schemaArray"": {
            ""type"": ""array"",
            ""minItems"": 1,
            ""items"": { ""$ref"": ""#"" }
        },
        ""positiveInteger"": {
            ""type"": ""integer"",
            ""minimum"": 0
        },
        ""positiveIntegerDefault0"": {
            ""allOf"": [ { ""$ref"": ""#/definitions/positiveInteger"" }, { ""default"": 0 } ]
        },
        ""simpleTypes"": {
            ""enum"": [ ""array"", ""boolean"", ""integer"", ""null"", ""number"", ""object"", ""string"" ]
        },
        ""stringArray"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" },
            ""minItems"": 1,
            ""uniqueItems"": true
        }
    },
    ""type"": ""object"",
    ""properties"": {
        ""id"": {
            ""type"": ""string"",
            ""format"": ""uri""
        },
        ""$schema"": {
            ""type"": ""string"",
            ""format"": ""uri""
        },
        ""title"": {
            ""type"": ""string""
        },
        ""description"": {
            ""type"": ""string""
        },
        ""default"": {},
        ""multipleOf"": {
            ""type"": ""number"",
            ""minimum"": 0,
            ""exclusiveMinimum"": true
        },
        ""maximum"": {
            ""type"": ""number""
        },
        ""exclusiveMaximum"": {
            ""type"": ""boolean"",
            ""default"": false
        },
        ""minimum"": {
            ""type"": ""number""
        },
        ""exclusiveMinimum"": {
            ""type"": ""boolean"",
            ""default"": false
        },
        ""maxLength"": { ""$ref"": ""#/definitions/positiveInteger"" },
        ""minLength"": { ""$ref"": ""#/definitions/positiveIntegerDefault0"" },
        ""pattern"": {
            ""type"": ""string"",
            ""format"": ""regex""
        },
        ""additionalItems"": {
            ""anyOf"": [
                { ""type"": ""boolean"" },
                { ""$ref"": ""#"" }
            ],
            ""default"": {}
        },
        ""items"": {
            ""anyOf"": [
                { ""$ref"": ""#"" },
                { ""$ref"": ""#/definitions/schemaArray"" }
            ],
            ""default"": {}
        },
        ""maxItems"": { ""$ref"": ""#/definitions/positiveInteger"" },
        ""minItems"": { ""$ref"": ""#/definitions/positiveIntegerDefault0"" },
        ""uniqueItems"": {
            ""type"": ""boolean"",
            ""default"": false
        },
        ""maxProperties"": { ""$ref"": ""#/definitions/positiveInteger"" },
        ""minProperties"": { ""$ref"": ""#/definitions/positiveIntegerDefault0"" },
        ""required"": { ""$ref"": ""#/definitions/stringArray"" },
        ""additionalProperties"": {
            ""anyOf"": [
                { ""type"": ""boolean"" },
                { ""$ref"": ""#"" }
            ],
            ""default"": {}
        },
        ""definitions"": {
            ""type"": ""object"",
            ""additionalProperties"": { ""$ref"": ""#"" },
            ""default"": {}
        },
        ""properties"": {
            ""type"": ""object"",
            ""additionalProperties"": { ""$ref"": ""#"" },
            ""default"": {}
        },
        ""patternProperties"": {
            ""type"": ""object"",
            ""additionalProperties"": { ""$ref"": ""#"" },
            ""default"": {}
        },
        ""dependencies"": {
            ""type"": ""object"",
            ""additionalProperties"": {
                ""anyOf"": [
                    { ""$ref"": ""#"" },
                    { ""$ref"": ""#/definitions/stringArray"" }
                ]
            }
        },
        ""enum"": {
            ""type"": ""array"",
            ""minItems"": 1,
            ""uniqueItems"": true
        },
        ""type"": {
            ""anyOf"": [
                { ""$ref"": ""#/definitions/simpleTypes"" },
                {
                    ""type"": ""array"",
                    ""items"": { ""$ref"": ""#/definitions/simpleTypes"" },
                    ""minItems"": 1,
                    ""uniqueItems"": true
                }
            ]
        },
        ""allOf"": { ""$ref"": ""#/definitions/schemaArray"" },
        ""anyOf"": { ""$ref"": ""#/definitions/schemaArray"" },
        ""oneOf"": { ""$ref"": ""#/definitions/schemaArray"" },
        ""not"": { ""$ref"": ""#"" }
    },
    ""dependencies"": {
        ""exclusiveMaximum"": [ ""maximum"" ],
        ""exclusiveMinimum"": [ ""minimum"" ]
    },
    ""default"": {}
}");

            Assert.AreEqual(new Uri("http://json-schema.org/draft-04/schema#"), schema.Id);
        }

        [Test]
        public void ErrorPathWhenFailureReadingDeferedReference()
        {
            string schemaJson = @"{
  ""type"":""object"",
  ""properties"":
  {
    ""name"":{""$ref"":""#/definitions/def1""}
  },
  ""definitions"":
  {
    ""def1"":{""type"":""invalid""}
  }
}";

            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                    schemaReader.ReadRoot(new JsonTextReader(new StringReader(schemaJson)));
                },
                "Invalid JSON schema type: invalid. Path 'definitions.def1.type', line 9, position 29.");
        }

        [Test]
        public void ErrorPathWhenFailureReadingDeferedReference_Array()
        {
            string schemaJson = @"{
  ""type"":""object"",
  ""properties"":
  {
    ""name"":{""$ref"":""#/definitions/0/def1/0""}
  },
  ""definitions"":
  [
    {
      ""def1"":[{""type"":""invalid""}]
    }
  ]
}";

            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                    schemaReader.ReadRoot(new JsonTextReader(new StringReader(schemaJson)));
                },
                // path here is wrong, will be fixed in future json.net release
                "Invalid JSON schema type: invalid. Path 'definitions[0].def1[0]type', line 10, position 32.");

            JObject o = JObject.Parse(schemaJson);

            JToken token = o["definitions"][0]["def1"][0]["type"];

            Console.WriteLine(token.Path);

            JToken selectedToken = o.SelectToken("definitions[0].def1[0].type");

            Assert.AreEqual(token, selectedToken);
        }

        [Test]
        public void ErrorPathWhenFailureReadingDeferedReference_Nested()
        {
            string schemaJson = @"{
  ""type"":""object"",
  ""properties"": {
    ""name"":{""$ref"":""#/definitions/def1""}
  },
  ""definitions"": {
    ""def1"": {
      ""type"":""array"",
      ""properties"": {
        ""name"":{""$ref"":""#/definitions/def1/def2""}
      },
      ""def2"": {
        ""type"":""invalid""
      }
    }
  }
}";

            ExceptionAssert.Throws<JSchemaReaderException>(
                () =>
                {
                    JSchemaReader schemaReader = new JSchemaReader(JSchemaDummyResolver.Instance);
                    schemaReader.ReadRoot(new JsonTextReader(new StringReader(schemaJson)));
                },
                "Invalid JSON schema type: invalid. Path 'definitions.def1.def2.type', line 13, position 25.");
        }

        [Test]
        public void DuplicateIdInDefinition()
        {
            string schemaJson = @"{
    ""id"": ""http://json-schema.org/draft-04/schema#"",
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""definitions"": {
        ""positiveInteger"": {
            ""type"": ""integer"",
            ""minimum"": 1
        },
        ""resFormBodyTypes"": {
            ""enum"": [
                ""boolean"",
                ""integer"",
                ""timestamp"",
                ""date"",
                ""string""
            ]
        },
        ""stringArray"": {
            ""type"": ""array"",
            ""items"": {
                ""type"": ""string""
            },
            ""minItems"": 1,
            ""uniqueItems"": true
        },
        ""rfType"": {
            ""id"": ""http://json-schema.org/draft-04/schema#"",
            ""$schema"": ""http://json-schema.org/draft-04/schema#"",
            ""type"": ""object"",
            ""properties"": {
            },
            ""additionalProperties"": false,
            ""required"": [""type""]
        }
    },
    ""type"": ""object"",
    ""properties"": {
        ""resFormBody"": {
            ""type"": ""object"",
            ""properties"": {
                ""properties"": {
                    ""type"": ""object"",
                    ""properties"": {
                    },
                    ""patternProperties"": {
                        ""^[a-z0-9_]+$"": {
                            ""$ref"": ""#/definitions/rfType""
                        }
                    },
                    ""additionalProperties"": false
                }
            },
            ""required"": [""properties""],
            ""additionalProperties"": false
        }
    },
    ""required"": [
        ""resFormName""
    ],
    ""additionalProperties"": false
}";

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema s = JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual("Duplicate schema id 'http://json-schema.org/draft-04/schema#' encountered.", errors[0].Message);
            Assert.AreEqual(ErrorType.Id, errors[0].ErrorType);
            Assert.AreEqual((JSchema)s.ExtensionData["definitions"]["rfType"], errors[0].Schema);

            string expected = @"{
  ""id"": ""http://json-schema.org/draft-04/schema#"",
  ""definitions"": {
    ""positiveInteger"": {
      ""type"": ""integer"",
      ""minimum"": 1
    },
    ""resFormBodyTypes"": {
      ""enum"": [
        ""boolean"",
        ""integer"",
        ""timestamp"",
        ""date"",
        ""string""
      ]
    },
    ""stringArray"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""string""
      },
      ""minItems"": 1,
      ""uniqueItems"": true
    },
    ""rfType"": {
      ""id"": ""http://json-schema.org/draft-04/schema#"",
      ""$schema"": ""http://json-schema.org/draft-04/schema#"",
      ""type"": ""object"",
      ""additionalProperties"": false,
      ""required"": [
        ""type""
      ]
    }
  },
  ""type"": ""object"",
  ""additionalProperties"": false,
  ""properties"": {
    ""resFormBody"": {
      ""type"": ""object"",
      ""additionalProperties"": false,
      ""properties"": {
        ""properties"": {
          ""type"": ""object"",
          ""additionalProperties"": false,
          ""patternProperties"": {
            ""^[a-z0-9_]+$"": {
              ""$ref"": ""#""
            }
          }
        }
      },
      ""required"": [
        ""properties""
      ]
    }
  },
  ""required"": [
    ""resFormName""
  ]
}";

            string writtenJson = s.ToString();

            StringAssert.AreEqual(expected, writtenJson);
        }

        [Test]
        public void DuplicateIdInDefinition2()
        {
            string schemaJson = @"{
    ""id"": ""http://json-schema.org/draft-04/schema#"",
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""title"": ""Resource Form Schema"",
    ""description"": ""Core GiS.IDM schema meta-schema"",
    ""type"": ""object"",
    ""properties"": {
        ""resFormBody1"": {
            ""id"": ""http://test#"",
            ""title"": ""Resource Form Type Schema 1"",
            ""type"": ""object""
        },
        ""resFormBody2"": {
            ""id"": ""http://test#"",
            ""title"": ""Resource Form Type Schema 2"",
            ""type"": ""object""
        },
        ""resFormBody3"": {
            ""$ref"": ""http://test#""
        }
    },
    ""required"": [
        ""resFormName""
    ],
    ""additionalProperties"": false
}";

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema s = JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual("Duplicate schema id 'http://test#' encountered.", errors[0].Message);
            Assert.AreEqual(ErrorType.Id, errors[0].ErrorType);
            Assert.AreEqual(s.Properties["resFormBody2"], errors[0].Schema);
            Assert.AreEqual("http://test#", errors[0].SchemaId.OriginalString);

            Assert.AreEqual(s.Properties["resFormBody1"], s.Properties["resFormBody3"]);
        }

        [Test]
        public void InvalidPattern()
        {
            string schemaJson = @"{
    ""id"": ""http://goshes.com/format/schema#"",
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""description"": ""Schema for goshes format"",
    ""type"": ""array"",
      ""items"":{
        ""oneOf"":[
          {""$ref"": ""#/definitions/SceneInformationInput""}
        ]
      },


    ""definitions"": {

        ""SceneInformationInput"": {

          ""properties"":{
                  ""inputId"":{
                    ""type"":""string"",
                    ""pattern"": ""^SceneInformationInput$""
                  },
                  ""data"":{""$ref"":""#/definitions/SceneInformationData""}

           },

           ""required"":[""inputId"", ""data""],

                  ""additionalProperties"": false
        } ,

        ""Date"" : {
           ""type"": ""object"",
           ""properties"":{
                ""name"":{
                   ""type"":""string"",
                   ""pattern"":""^[0-2][0-9]{3}-((0[1-9])|(1[0-2]))-(([0-2][0-9])|3[0-1]T[0)$""
                }
           }
        },

        ""SceneInformationData"":{
                ""type"": ""object"",
            ""properties"":{

                ""name"":{
                   ""type"":""string""
                },
                ""author"":{
                        ""type"" : ""string""
                },
                ""createDate"":{ ""$ref"" : ""#/definitions/Date"" }
                }
            },

                ""required"" : [""name"", ""author""]
        }

    }
}";

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema s = JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(1, errors.Count);

            Assert.AreEqual(@"Could not parse regex pattern '^[0-2][0-9]{3}-((0[1-9])|(1[0-2]))-(([0-2][0-9])|3[0-1]T[0)$'. Regex parser error: parsing ""^[0-2][0-9]{3}-((0[1-9])|(1[0-2]))-(([0-2][0-9])|3[0-1]T[0)$"" - Unterminated [] set.", errors[0].Message);
            Assert.AreEqual(ErrorType.Pattern, errors[0].ErrorType);
            Assert.AreEqual("http://goshes.com/format/schema#/definitions/Date/properties/name", errors[0].SchemaId.OriginalString);
            Assert.AreEqual(s.Items[0].OneOf[0].Properties["data"].Properties["createDate"].Properties["name"], errors[0].Schema);
        }

        [Test]
        public void InvalidPattern2()
        {
            string schemaJson = @"{
	""title"": ""JSON schema for DNX project.json files"",
	""$schema"": ""http://json-schema.org/draft-04/schema#"",

	""type"": ""object"",

	""properties"": {
		""authors"": {
			""type"": ""array"",
			""items"": {
				""type"": ""string"",
				""uniqueItems"": true,
                ""pattern"":""[]""
			}
		}
	}
}";

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema s = JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(1, errors.Count);

            Assert.AreEqual(@"Could not parse regex pattern '[]'. Regex parser error: parsing ""[]"" - Unterminated [] set.", errors[0].Message);
            Assert.AreEqual(ErrorType.Pattern, errors[0].ErrorType);
            Assert.AreEqual("#/properties/authors/items/0", errors[0].SchemaId.OriginalString);
            Assert.AreEqual(s.Properties["authors"].Items[0], errors[0].Schema);
        }

        [Test]
        public void InvalidPatternInResolvedSchema()
        {
            string schemaJson = @"{
	""title"": ""JSON schema for DNX project.json files"",
	""$schema"": ""http://json-schema.org/draft-04/schema#"",

	""type"": ""object"",

	""properties"": {
		""authors"": {
			""$ref"": ""http://test#""
		}
	}
}";

            string resolvedSchemaJson = @"{
	""id"": ""http://test#"",
	""$schema"": ""http://json-schema.org/draft-04/schema#"",

	""properties"": {
		""name"": {
	        ""type"": ""string"",
	        ""uniqueItems"": true,
            ""pattern"":""[]""
		}
	}
}";

            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();
            resolver.Add(new Uri("http://test"), resolvedSchemaJson);

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.Resolver = resolver;
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema s = JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(1, errors.Count);

            Assert.AreEqual(@"Could not parse regex pattern '[]'. Regex parser error: parsing ""[]"" - Unterminated [] set.", errors[0].Message);
            Assert.AreEqual(ErrorType.Pattern, errors[0].ErrorType);
            Assert.AreEqual("http://test/#/properties/name", errors[0].SchemaId.OriginalString);
            Assert.AreEqual(s.Properties["authors"].Properties["name"], errors[0].Schema);
        }

        [Test]
        public void InvalidPatternInDeferredResolvedSchema()
        {
            string schemaJson = @"{
	""title"": ""JSON schema for DNX project.json files"",
	""$schema"": ""http://json-schema.org/draft-04/schema#"",

	""type"": ""object"",

	""properties"": {
		""authors"": {
			""$ref"": ""http://test#/definitions/authors""
		},
		""authors2"": {
			""pattern"":""[]""
		}
	}
}";

            string resolvedSchemaJson = @"{
	""id"": ""http://test#"",
	""$schema"": ""http://json-schema.org/draft-04/schema#"",

	""definitions"": {
		""authors"": {
			""type"": ""array"",
			""items"": {
				""type"": ""string"",
				""uniqueItems"": true,
                ""pattern"":""[]""
			}
		}
    }
}";

            JSchemaPreloadedResolver resolver = new JSchemaPreloadedResolver();
            resolver.Add(new Uri("http://test"), resolvedSchemaJson);

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.Resolver = resolver;
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema s = JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(2, errors.Count);

            Assert.AreEqual(@"Could not parse regex pattern '[]'. Regex parser error: parsing ""[]"" - Unterminated [] set.", errors[0].Message);
            Assert.AreEqual(ErrorType.Pattern, errors[0].ErrorType);
            Assert.AreEqual("http://test/#/definitions/authors/items/0", errors[0].SchemaId.OriginalString);
            Assert.AreEqual(s.Properties["authors"].Items[0], errors[0].Schema);
            Assert.AreEqual("http://test", errors[0].Schema.BaseUri.OriginalString);

            Assert.AreEqual(null, errors[1].Schema.BaseUri);
        }

        [Test]
        public void DuplicatedIdMultipleTimes()
        {
            string schemaJson = @"{
  ""$schema"": ""http://json-schema.org/draft-04/schema#"",
  ""id"": ""/"",
  ""type"": ""object"",
  ""properties"": {
    ""state"": {
      ""id"": ""state"",
      ""type"": ""string""
    },
    ""hotelData"": {
      ""id"": ""hotelData"",
      ""type"": ""object"",
      ""properties"": {
        ""id"": {
          ""id"": ""id"",
          ""type"": ""integer""
        },
        ""name"": {
          ""id"": ""name"",
          ""type"": ""string""
        },
        ""address"": {
          ""id"": ""address"",
          ""type"": ""string""
        },
        ""zip"": {
          ""id"": ""zip"",
          ""type"": ""string""
        },
        ""city"": {
          ""id"": ""city"",
          ""type"": ""string""
        },
        ""phone"": {
          ""id"": ""phone"",
          ""type"": ""string""
        },
        ""category"": {
          ""id"": ""category"",
          ""type"": ""integer""
        },
        ""superior"": {
          ""id"": ""superior"",
          ""type"": ""boolean""
        },
        ""homepage"": {
          ""id"": ""homepage"",
          ""type"": ""string""
        },
        ""offers"": {
          ""id"": ""offers"",
          ""type"": ""array"",
          ""items"": {
            ""id"": ""215"",
            ""type"": ""object"",
            ""properties"": {
              ""prepaidRate"": {
                ""id"": ""prepaidRate"",
                ""type"": ""integer""
              },
              ""description"": {
                ""id"": ""description"",
                ""type"": ""string""
              },
              ""requiresCreditCard"": {
                ""id"": ""requiresCreditCard"",
                ""type"": ""integer""
              },
              ""price"": {
                ""id"": ""price"",
                ""type"": ""object"",
                ""properties"": {
                  ""euroValue"": {
                    ""id"": ""euroValue"",
                    ""type"": ""integer""
                  },
                  ""value"": {
                    ""id"": ""value"",
                    ""type"": ""integer""
                  },
                  ""currency"": {
                    ""id"": ""currency"",
                    ""type"": ""string""
                  },
                  ""native"": {
                    ""id"": ""native"",
                    ""type"": ""string""
                  }
                }
              },
              ""roomType"": {
                ""id"": ""roomType"",
                ""type"": ""string""
              },
              ""partnerId"": {
                ""id"": ""partnerId"",
                ""type"": ""integer""
              },
              ""partnerReferenceId"": {
                ""id"": ""partnerReferenceId"",
                ""type"": ""string""
              },
              ""link"": {
                ""id"": ""link"",
                ""type"": ""string""
              },
              ""expressBookingLink"": {
                ""id"": ""expressBookingLink"",
                ""type"": ""string""
              },
              ""breakfastPrice"": {
                ""id"": ""breakfastPrice"",
                ""type"": ""string""
              },
              ""breakfastIncluded"": {
                ""id"": ""breakfastIncluded"",
                ""type"": ""integer""
              },
              ""roomsLeftForDeal"": {
                ""id"": ""roomsLeftForDeal"",
                ""type"": ""integer""
              },
              ""payLater"": {
                ""id"": ""payLater"",
                ""type"": ""integer""
              },
              ""cancellable"": {
                ""id"": ""cancellable"",
                ""type"": ""integer""
              }
            }
          }
        },
        ""overallLiking"": {
          ""id"": ""overallLiking"",
          ""type"": ""integer""
        },
        ""imageUrl"": {
          ""id"": ""imageUrl"",
          ""type"": ""string""
        },
        ""imageSquareUrl"": {
          ""id"": ""imageSquareUrl"",
          ""type"": ""string""
        },
        ""contentStats"": {
          ""id"": ""contentStats"",
          ""type"": ""object"",
          ""properties"": {
            ""countOpinion"": {
              ""id"": ""countOpinion"",
              ""type"": ""integer""
            },
            ""countPartner"": {
              ""id"": ""countPartner"",
              ""type"": ""integer""
            },
            ""countImage"": {
              ""id"": ""countImage"",
              ""type"": ""integer""
            },
            ""countDescription"": {
              ""id"": ""countDescription"",
              ""type"": ""integer""
            }
          }
        },
        ""partnerRatings"": {
          ""id"": ""partnerRatings"",
          ""type"": ""array"",
          ""items"": {
            ""type"": ""object"",
            ""properties"": {
              ""partnerName"": {
                ""id"": ""partnerName"",
                ""type"": ""string""
              },
              ""partnerId"": {
                ""id"": ""partnerId"",
                ""type"": ""integer""
              },
              ""overallLiking"": {
                ""id"": ""overallLiking"",
                ""type"": ""integer""
              },
              ""partnerRating"": {
                ""id"": ""partnerRating"",
                ""type"": [
                  ""integer"",
                  ""null""
                ]
              },
              ""partnerMaxRating"": {
                ""id"": ""partnerMaxRating"",
                ""type"": ""integer""
              },
              ""reviewCount"": {
                ""id"": ""reviewCount"",
                ""type"": ""integer""
              },
              ""url"": {
                ""id"": ""url"",
                ""type"": ""string""
              }
            }
          }
        },
        ""ratings"": {
          ""id"": ""ratings"",
          ""type"": ""array"",
          ""items"": {}
        },
        ""fields"": {
          ""id"": ""fields"",
          ""type"": ""array"",
          ""items"": {
            ""id"": ""67"",
            ""type"": ""object"",
            ""properties"": {
              ""group"": {
                ""id"": ""group"",
                ""type"": ""string""
              },
              ""group_id"": {
                ""id"": ""group_id"",
                ""type"": ""integer""
              },
              ""field"": {
                ""id"": ""field"",
                ""type"": ""string""
              },
              ""field_id"": {
                ""id"": ""field_id"",
                ""type"": ""integer""
              },
              ""label"": {
                ""id"": ""label"",
                ""type"": ""string""
              },
              ""label_id"": {
                ""id"": ""label_id"",
                ""type"": ""integer""
              }
            }
          }
        },
        ""description"": {
          ""id"": ""description"",
          ""type"": ""string""
        },
        ""images"": {
          ""id"": ""images"",
          ""type"": ""array"",
          ""items"": {
            ""id"": ""24"",
            ""type"": ""object"",
            ""properties"": {
              ""urlSmall"": {
                ""id"": ""urlSmall"",
                ""type"": ""string""
              },
              ""urlMedium"": {
                ""id"": ""urlMedium"",
                ""type"": ""string""
              },
              ""urlBig"": {
                ""id"": ""urlBig"",
                ""type"": ""string""
              },
              ""urlExtraBig"": {
                ""id"": ""urlExtraBig"",
                ""type"": ""string""
              }
            }
          }
        },
        ""numberOfImages"": {
          ""id"": ""numberOfImages"",
          ""type"": ""integer""
        },
        ""location"": {
          ""id"": ""location"",
          ""type"": ""object"",
          ""properties"": {
            ""id"": {
              ""id"": ""id"",
              ""type"": ""integer""
            },
            ""name"": {
              ""id"": ""name"",
              ""type"": ""string""
            },
            ""coords"": {
              ""id"": ""coords"",
              ""type"": ""object"",
              ""properties"": {
                ""longitude"": {
                  ""id"": ""longitude"",
                  ""type"": ""number""
                },
                ""latitude"": {
                  ""id"": ""latitude"",
                  ""type"": ""number""
                }
              }
            }
          }
        },
        ""numberOfReviews"": {
          ""id"": ""numberOfReviews"",
          ""type"": ""integer""
        },
        ""isBookmark"": {
          ""id"": ""isBookmark"",
          ""type"": ""boolean""
        }
      }
    },
    ""partners"": {
      ""id"": ""partners"",
      ""type"": ""array"",
      ""items"": {
        ""id"": ""14"",
        ""type"": ""object"",
        ""properties"": {
          ""id"": {
            ""id"": ""id"",
            ""type"": ""integer""
          },
          ""name"": {
            ""id"": ""name"",
            ""type"": ""string""
          },
          ""state"": {
            ""id"": ""state"",
            ""type"": ""string""
          },
          ""imgSizeSColored"": {
            ""id"": ""imgSizeSColored"",
            ""type"": ""string""
          },
          ""imgSizeSGrayscale"": {
            ""id"": ""imgSizeSGrayscale"",
            ""type"": ""string""
          },
          ""imgSizeMXColored"": {
            ""id"": ""imgSizeMXColored"",
            ""type"": ""string""
          }
        }
      }
    },
    ""resultInfo"": {
      ""id"": ""resultInfo"",
      ""type"": ""object"",
      ""properties"": {
        ""resultCount"": {
          ""id"": ""resultCount"",
          ""type"": ""integer""
        },
        ""orderBy"": {
          ""id"": ""orderBy"",
          ""type"": ""object"",
          ""properties"": {
            ""type"": {
              ""id"": ""type"",
              ""type"": ""string""
            },
            ""flag"": {
              ""id"": ""flag"",
              ""type"": ""string""
            }
          }
        },
        ""paging"": {
          ""id"": ""paging"",
          ""type"": ""object"",
          ""properties"": {
            ""limit"": {
              ""id"": ""limit"",
              ""type"": ""integer""
            },
            ""offset"": {
              ""id"": ""offset"",
              ""type"": ""integer""
            }
          }
        }
      }
    },
    ""pathInfo"": {
      ""id"": ""pathInfo"",
      ""type"": ""object"",
      ""properties"": {
        ""isPath"": {
          ""id"": ""isPath"",
          ""type"": ""boolean""
        },
        ""isCity"": {
          ""id"": ""isCity"",
          ""type"": ""boolean""
        },
        ""path"": {
          ""id"": ""path"",
          ""type"": ""object"",
          ""properties"": {
            ""id"": {
              ""id"": ""id"",
              ""type"": ""integer""
            },
            ""name"": {
              ""id"": ""name"",
              ""type"": ""string""
            },
            ""coords"": {
              ""id"": ""coords"",
              ""type"": ""object"",
              ""properties"": {
                ""longitude"": {
                  ""id"": ""longitude"",
                  ""type"": ""number""
                },
                ""latitude"": {
                  ""id"": ""latitude"",
                  ""type"": ""number""
                }
              }
            }
          }
        },
        ""coords"": {
          ""id"": ""coords"",
          ""type"": ""null""
        }
      }
    },
    ""activeTests"": {
      ""id"": ""activeTests"",
      ""type"": ""array"",
      ""items"": {
        ""type"": ""integer""
      }
    }
  },
  ""required"": [
    ""state"",
    ""hotelData"",
    ""partners"",
    ""resultInfo"",
    ""pathInfo"",
    ""activeTests""
  ]
}";

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(14, errors.Count);
            Assert.AreEqual("Duplicate schema id 'partnerId' encountered.", errors[0].Message);
        }

        [Test]
        public void InvalidPatternPropertyRegex()
        {
            string schemaJson = @"{
  ""$schema"": ""http://json-schema.org/draft-04/schema"",
  ""definitions"": {
    ""pais"": {
      ""type"": ""object""
    }
  },
  ""properties"": {
    ""$schema"": {
      ""type"": ""string""
    },
    ""paises"": {
      ""patternProperties"": {
        ""[]"": {
          ""$ref"": ""#/definitions/pais""
        }
      }
    }
  }
}";

            List<ValidationError> errors = new List<ValidationError>();

            JSchemaReaderSettings settings = new JSchemaReaderSettings();
            settings.ValidationEventHandler += (o, e) => errors.Add(e.ValidationError);

            JSchema.Parse(schemaJson, settings);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(@"Could not parse regex pattern '[]'. Regex parser error: parsing ""[]"" - Unterminated [] set.", errors[0].Message);
            Assert.AreEqual(new Uri("#/properties/paises", UriKind.Relative), errors[0].SchemaId);
            Assert.AreEqual(ErrorType.PatternProperties, errors[0].ErrorType);
        }

        [Test]
        public void InvalidId()
        {
            string schemaJson = @"{
  ""$schema"": ""http://json-schema.org/draft-04/schema"",
  ""id"": ""http://""
}";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchema.Parse(schemaJson);
            }, "Error parsing id 'http://'. Id must be a valid URI. Path 'id', line 3, position 18.");
        }

        [Test]
        public void InvalidRefId()
        {
            string schemaJson = @"{
  ""id"": ""#root"",
  ""$schema"": ""http://json-schema.org/draft-04/schema#"",
  ""title"": ""command"",
  ""type"": ""object"",
  ""oneOf"": [
    {
      ""$ref"": ""file:system.json#/definitions/username""
    }
  ]
}";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchema.Parse(schemaJson);
            }, "Error resolving schema reference 'file:system.json#/definitions/username' in the scope '#root'. The resolved reference must be a valid URI. Path 'oneOf[0]', line 7, position 6.");
        }

        [Test]
        public void InvalidSchemaId()
        {
            string schemaJson = @"{
  ""id"": ""#root"",
  ""$schema"": ""http://"",
  ""title"": ""command"",
  ""type"": ""object""
}";

            ExceptionAssert.Throws<JSchemaReaderException>(() =>
            {
                JSchema.Parse(schemaJson);
            }, "Error parsing id 'http://'. Id must be a valid URI. Path '$schema', line 3, position 23.");
        }
    }
}
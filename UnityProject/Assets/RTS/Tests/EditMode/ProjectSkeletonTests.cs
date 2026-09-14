using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rts.Tests.EditMode
{
    public sealed class ProjectSkeletonTests
    {
        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string[] references = Array.Empty<string>();
            public bool noEngineReferences = false;
            public bool autoReferenced = false;
            public bool overrideReferences = false;
            public string[] precompiledReferences = Array.Empty<string>();
            public bool allowUnsafeCode = false;
        }

        [TestCase("Contracts", new string[0])]
        [TestCase("Decision", new[] { "Rts.Contracts" })]
        [TestCase("Simulation", new[] { "Rts.Contracts", "Rts.Decision" })]
        [TestCase("Replay", new[] { "Rts.Contracts" })]
        [TestCase("Application", new[] { "Rts.Contracts", "Rts.Simulation", "Rts.Replay" })]
        public void PureAssembliesRespectTheDependencyBoundary(string name, string[] allowed)
        {
            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(
                Path.Combine(UnityEngine.Application.dataPath, "RTS", name, "Rts." + name + ".asmdef")));
            Assert.That(definition.references, Is.EquivalentTo(allowed));
            Assert.That(definition.noEngineReferences, Is.True);
            Assert.That(definition.autoReferenced, Is.False);
            Assert.That(definition.overrideReferences, Is.True);
            Assert.That(definition.precompiledReferences, Is.Empty);
            Assert.That(definition.allowUnsafeCode, Is.False);

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .Single(a => a.GetName().Name == "Rts." + name);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal), Is.False);
                Assert.That(reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal), Is.False);
                if (reference.Name.StartsWith("Rts.", StringComparison.Ordinal))
                    Assert.That(allowed, Does.Contain(reference.Name));
            }
        }

        [Test]
        public void EachMarkerIsCompiledIntoItsOwnAssembly()
        {
            Type[] markers =
            {
                typeof(Contracts.ContractsAssemblyMarker),
                typeof(Decision.DecisionAssemblyMarker),
                typeof(Simulation.SimulationAssemblyMarker),
                typeof(Replay.ReplayAssemblyMarker),
                typeof(Application.ApplicationAssemblyMarker),
                typeof(Presentation.PresentationAssemblyMarker),
                typeof(UnityHost.UnityHostAssemblyMarker),
                typeof(Editor.EditorAssemblyMarker),
                typeof(TestsEditModeAssemblyMarker)
            };
            foreach (var marker in markers)
                Assert.That(marker.Assembly.GetName().Name, Is.EqualTo(marker.Namespace), marker.FullName);
        }

        [Test]
        public void GraphicsAndEveryQualityLevelUseTheSavedPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(
                "Assets/Settings/RtsUniversalRenderPipeline.asset");
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.GetType().FullName,
                Is.EqualTo("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset"));
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.EqualTo(pipeline));
            var serializedPipeline = new SerializedObject(pipeline);
            var renderers = serializedPipeline.FindProperty("m_RendererDataList");
            Assert.That(renderers.arraySize, Is.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(0).objectReferenceValue),
                Is.EqualTo("Assets/Settings/RtsUniversalRenderer.asset"));
            Assert.That(serializedPipeline.FindProperty("m_DefaultRendererIndex").intValue, Is.Zero);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                Assert.That(QualitySettings.GetRenderPipelineAssetAt(i), Is.EqualTo(pipeline), QualitySettings.names[i]);
        }

        [Test]
        public void VersionControlSettingsAreTextAndVisibleMetaFiles()
        {
            Assert.That(EditorSettings.serializationMode, Is.EqualTo(SerializationMode.ForceText));
            Assert.That(UnityEditor.VersionControlSettings.mode, Is.EqualTo("Visible Meta Files"));
        }
    }
}

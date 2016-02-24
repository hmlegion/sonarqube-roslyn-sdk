﻿//-----------------------------------------------------------------------
// <copyright file="AnalyzerPluginGeneratorTests.cs" company="SonarSource SA and Microsoft Corporation">
//   Copyright (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
//   Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet;
using SonarLint.XmlDescriptor;
using SonarQube.Plugins.Roslyn.CommandLine;
using SonarQube.Plugins.Test.Common;
using System.IO;
using static SonarQube.Plugins.Roslyn.RoslynPluginGeneratorTests.RemoteRepoBuilder;

namespace SonarQube.Plugins.Roslyn.RoslynPluginGeneratorTests
{
    /// <summary>
    /// Tests for NuGetPackageHandler.cs
    /// </summary>
    [TestClass]
    public class AnalyzerPluginGeneratorTests
    {
        public TestContext TestContext { get; set; }

        private enum Node { Root, Child1, Child2, Grandchild1_1, Grandchild2_1, Grandchild2_2 };

        [TestMethod]
        public void Generate_NoAnalyzersFoundInPackage_GenerateFails()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");

            TestLogger logger = new TestLogger();

            // Create a fake remote repo containing a package that does not contain analyzers
            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);
            remoteRepoBuilder.CreatePackage("no.analyzers.id", "0.9", TestUtils.CreateTextFile("dummy.txt", outputDir), License.NotRequired /* no dependencies */ );
           
            NuGetPackageHandler nuGetHandler = new NuGetPackageHandler(remoteRepoBuilder.FakeRemoteRepo, GetLocalNuGetDownloadDir(), logger);
            AnalyzerPluginGenerator apg = new AnalyzerPluginGenerator(nuGetHandler, logger);
            ProcessedArgs args = CreateArgs("no.analyzers.id", "0.9", "cs", null, false, outputDir);

            // Act
            bool result = apg.Generate(args);

            // Assert
            Assert.IsFalse(result, "Expecting generation to fail");
            logger.AssertWarningsLogged(1);
            AssertSqaleTemplateDoesNotExist(outputDir);
        }

        [TestMethod]
        public void Generate_LicenseAcceptanceNotRequired_Succeeds()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");
            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);

            // Multi-level dependencies: no package requires license acceptence
            IPackage grandchild = CreatePackageWithAnalyzer(remoteRepoBuilder, "grandchild.id", "1.2", License.NotRequired /* no dependencies */);
            IPackage child = CreatePackageWithAnalyzer(remoteRepoBuilder, "child.id", "1.1", License.NotRequired, grandchild);
            CreatePackageWithAnalyzer(remoteRepoBuilder, "parent.id", "1.0", License.NotRequired, child);

            TestLogger logger = new TestLogger();
            AnalyzerPluginGenerator apg = CreateTestSubjectWithFakeRemoteRepo(remoteRepoBuilder, logger);

            // 1. Acceptance not required -> succeeds if accept = false
            ProcessedArgs args = CreateArgs("parent.id", "1.0", "cs", null, false /* accept licenses */ , outputDir);
            bool result = apg.Generate(args);
            Assert.IsTrue(result, "Generator should succeed if there are no licenses to accept");

            logger.AssertErrorsLogged(0);
            logger.AssertWarningNotLogged("parent.id"); // not expecting warnings about packages that don't require acceptance
            logger.AssertWarningNotLogged("child.id");
            logger.AssertWarningNotLogged("grandchild.id");

            // 2. Acceptance not required -> succeeds if accept = true
            args = CreateArgs("parent.id", "1.0", "cs", null, true /* accept licenses */ , outputDir);
            result = apg.Generate(args);
            Assert.IsTrue(result, "Generator should succeed if there are no licenses to accept");

            logger.AssertErrorsLogged(0);
            logger.AssertWarningNotLogged("parent.id"); // not expecting warnings about packages that don't require acceptance
            logger.AssertWarningNotLogged("child.id");
            logger.AssertWarningNotLogged("grandchild.id");
        }

        [TestMethod]
        public void Generate_LicenseAcceptanceRequiredByMainPackage()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");
            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);

            // Parent and child: only parent requires license
            IPackage child = CreatePackageWithAnalyzer(remoteRepoBuilder, "child.id", "1.1", License.NotRequired);
            CreatePackageWithAnalyzer(remoteRepoBuilder, "parent.requiredAccept.id", "1.0", License.Required, child);

            TestLogger logger = new TestLogger();
            AnalyzerPluginGenerator apg = CreateTestSubjectWithFakeRemoteRepo(remoteRepoBuilder, logger);

            // 1. User does not accept -> fails with error
            ProcessedArgs args = CreateArgs("parent.requiredAccept.id", "1.0", "cs", null, false /* accept licenses */ , outputDir);
            bool result = apg.Generate(args);
            Assert.IsFalse(result, "Generator should fail because license has not been accepted");

            logger.AssertSingleErrorExists("parent.requiredAccept.id", "1.0"); // error listing the main package
            logger.AssertSingleWarningExists("parent.requiredAccept.id", "1.0"); // warning for each licensed package
            logger.AssertWarningsLogged(1);

            // 2. User accepts -> succeeds with warnings
            logger.Reset();
            args = CreateArgs("parent.requiredAccept.id", "1.0", "cs", null, true /* accept licenses */ , outputDir);
            result = apg.Generate(args);
            Assert.IsTrue(result, "Generator should succeed if licenses are accepted");

            logger.AssertSingleWarningExists(UIResources.APG_NGAcceptedPackageLicenses); // warning that licenses accepted
            logger.AssertSingleWarningExists("parent.requiredAccept.id", "1.0"); // warning for each licensed package
            logger.AssertWarningsLogged(2);
            logger.AssertErrorsLogged(0);
        }

        [TestMethod]
        public void Generate_LicenseAcceptanceRequiredByDependency()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");
            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);

            // Parent and child: only child requires license
            IPackage child = CreatePackageWithAnalyzer(remoteRepoBuilder, "child.requiredAccept.id", "2.0", License.Required);
            CreatePackageWithAnalyzer(remoteRepoBuilder, "parent.id", "1.0", License.NotRequired, child);

            TestLogger logger = new TestLogger();
            AnalyzerPluginGenerator apg = CreateTestSubjectWithFakeRemoteRepo(remoteRepoBuilder, logger);

            // 1. User does not accept -> fails with error
            ProcessedArgs args = CreateArgs("parent.id", "1.0", "cs", null, false /* accept licenses */ , outputDir);
            bool result = apg.Generate(args);
            Assert.IsFalse(result, "Generator should fail because license has not been accepted");

            logger.AssertSingleErrorExists("parent.id", "1.0"); // error listing the main package
            logger.AssertSingleWarningExists("child.requiredAccept.id", "2.0"); // warning for each licensed package
            logger.AssertWarningsLogged(1);

            // 2. User accepts -> succeeds with warnings
            logger.Reset();
            args = CreateArgs("parent.id", "1.0", "cs", null, true /* accept licenses */ , outputDir);
            result = apg.Generate(args);
            Assert.IsTrue(result, "Generator should succeed if licenses are accepted");

            logger.AssertSingleWarningExists(UIResources.APG_NGAcceptedPackageLicenses); // warning that licenses have been accepted
            logger.AssertSingleWarningExists("child.requiredAccept.id", "2.0"); // warning for each licensed package
            logger.AssertWarningsLogged(2);
            logger.AssertErrorsLogged(0);
        }

        [TestMethod]
        public void Generate_LicenseAcceptanceRequired_ByParentAndDependencies()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");
            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);

            // Multi-level: parent and some but not all dependencies require license acceptance
            IPackage grandchild1 = CreatePackageWithAnalyzer(remoteRepoBuilder, "grandchild1.requiredAccept.id", "3.0", License.Required);
            IPackage child1 = CreatePackageWithAnalyzer(remoteRepoBuilder, "child1.requiredAccept.id", "2.1", License.Required);
            IPackage child2 = CreatePackageWithAnalyzer(remoteRepoBuilder, "child2.id", "2.2", License.NotRequired, grandchild1);
            CreatePackageWithAnalyzer(remoteRepoBuilder, "parent.requiredAccept.id", "1.0", License.Required, child1, child2);

            TestLogger logger = new TestLogger();
            AnalyzerPluginGenerator apg = CreateTestSubjectWithFakeRemoteRepo(remoteRepoBuilder, logger);

            // 1. User does not accept -> fails with error
            ProcessedArgs args = CreateArgs("parent.requiredAccept.id", "1.0", "cs", null, false /* accept licenses */ , outputDir);
            bool result = apg.Generate(args);
            Assert.IsFalse(result, "Generator should fail because license has not been accepted");

            logger.AssertSingleErrorExists("parent.requiredAccept.id", "1.0"); // error referring to the main package

            logger.AssertSingleWarningExists("grandchild1.requiredAccept.id", "3.0"); // warning for each licensed package
            logger.AssertSingleWarningExists("child1.requiredAccept.id", "2.1");
            logger.AssertSingleWarningExists("parent.requiredAccept.id", "1.0");

            // 2. User accepts -> succeeds with warnings
            logger.Reset();
            args = CreateArgs("parent.requiredAccept.id", "1.0", "cs", null, true /* accept licenses */ , outputDir);
            result = apg.Generate(args);
            Assert.IsTrue(result, "Generator should succeed if licenses are accepted");

            logger.AssertSingleWarningExists(UIResources.APG_NGAcceptedPackageLicenses); // warning that licenses have been accepted
            logger.AssertSingleWarningExists("grandchild1.requiredAccept.id", "3.0"); // warning for each licensed package
            logger.AssertSingleWarningExists("child1.requiredAccept.id", "2.1");
            logger.AssertSingleWarningExists("parent.requiredAccept.id", "1.0");
            logger.AssertWarningsLogged(4);
        }

        [TestMethod]
        public void Generate_SqaleFileNotSpecified_TemplateFileCreated()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");

            TestLogger logger = new TestLogger();

            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);
            CreatePackageInFakeRemoteRepo(remoteRepoBuilder, "dummy.id", "1.1");

            NuGetPackageHandler nuGetHandler = new NuGetPackageHandler(remoteRepoBuilder.FakeRemoteRepo, GetLocalNuGetDownloadDir(), logger);

            string expectedTemplateSqaleFilePath = Path.Combine(outputDir, "dummy.id.1.1.sqale.template.xml");

            AnalyzerPluginGenerator apg = new AnalyzerPluginGenerator(nuGetHandler, logger);

            ProcessedArgs args = CreateArgs("dummy.id", "1.1", "cs", null, false, outputDir);

            // Act
            bool result = apg.Generate(args);

            // Assert
            Assert.IsTrue(result, "Expecting generation to have succeeded");
            Assert.IsTrue(File.Exists(expectedTemplateSqaleFilePath), "Expecting a template sqale file to have been created");
            this.TestContext.AddResultFile(expectedTemplateSqaleFilePath);
            logger.AssertSingleInfoMessageExists(expectedTemplateSqaleFilePath); // should be a message about the generated file
        }

        [TestMethod]
        public void Generate_ValidSqaleFileSpecified_TemplateFileNotCreated()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");

            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);
            AnalyzerPluginGenerator apg = CreateTestSubjectWithFakeRemoteRepo(remoteRepoBuilder);

            CreatePackageInFakeRemoteRepo(remoteRepoBuilder, "dummy.id", "1.1");

            // Create a dummy sqale file
            string dummySqaleFilePath = Path.Combine(outputDir, "inputSqale.xml");
            SqaleRoot dummySqale = new SqaleRoot();
            Serializer.SaveModel(dummySqale, dummySqaleFilePath);

            ProcessedArgs args = CreateArgs("dummy.id", "1.1", "cs", dummySqaleFilePath, false, outputDir);

            // Act
            bool result = apg.Generate(args);

            // Assert
            Assert.IsTrue(result, "Expecting generation to have succeeded");
            AssertSqaleTemplateDoesNotExist(outputDir);
        }

        [TestMethod]
        public void Generate_InvalidSqaleFileSpecified_GeneratorError()
        {
            // Arrange
            string outputDir = TestUtils.CreateTestDirectory(this.TestContext, ".out");

            TestLogger logger = new TestLogger();

            RemoteRepoBuilder remoteRepoBuilder = new RemoteRepoBuilder(this.TestContext);
            CreatePackageInFakeRemoteRepo(remoteRepoBuilder, "dummy.id", "1.1");

            NuGetPackageHandler nuGetHandler = new NuGetPackageHandler(remoteRepoBuilder.FakeRemoteRepo, GetLocalNuGetDownloadDir(), logger);

            // Create an invalid sqale file
            string dummySqaleFilePath = Path.Combine(outputDir, "invalidSqale.xml");
            File.WriteAllText(dummySqaleFilePath, "not valid xml");

            AnalyzerPluginGenerator apg = new AnalyzerPluginGenerator(nuGetHandler, logger);

            ProcessedArgs args = CreateArgs("dummy.id", "1.1", "cs", dummySqaleFilePath, false, outputDir);

            // Act
            bool result = apg.Generate(args);

            // Assert
            Assert.IsFalse(result, "Expecting generation to have failed");
            AssertSqaleTemplateDoesNotExist(outputDir);
            logger.AssertSingleErrorExists("invalidSqale.xml"); // expecting an error containing the invalid sqale file name
        }

        #region Private methods

        private static ProcessedArgs CreateArgs(string packageId, string packageVersion, string language, string sqaleFilePath, bool acceptLicenses, string outputDirectory)
        {
            ProcessedArgs args = new ProcessedArgs(
                packageId,
                new SemanticVersion(packageVersion),
                language,
                sqaleFilePath,
                acceptLicenses,
                outputDirectory);
            return args;
        }

        private AnalyzerPluginGenerator CreateTestSubjectWithFakeRemoteRepo(RemoteRepoBuilder remoteRepoBuilder)
        {
            return CreateTestSubjectWithFakeRemoteRepo(remoteRepoBuilder, new TestLogger());
        }

        private AnalyzerPluginGenerator CreateTestSubjectWithFakeRemoteRepo(RemoteRepoBuilder remoteRepoBuilder, TestLogger logger)
        {
            NuGetPackageHandler nuGetHandler = new NuGetPackageHandler(remoteRepoBuilder.FakeRemoteRepo, GetLocalNuGetDownloadDir(), logger);
            return new AnalyzerPluginGenerator(nuGetHandler, logger);
        }

        private static IPackage CreatePackageWithAnalyzer(RemoteRepoBuilder remoteRepoBuilder, string packageId, string packageVersion, License acceptanceRequired, params IPackage[] dependencies)
        {
            return remoteRepoBuilder.CreatePackage(packageId, packageVersion, typeof(RoslynAnalyzer11.CSharpAnalyzer).Assembly.Location, acceptanceRequired, dependencies);
        }

        private void CreatePackageInFakeRemoteRepo(RemoteRepoBuilder remoteRepoBuilder, string packageId, string packageVersion)
        {
            remoteRepoBuilder.CreatePackage(packageId, packageVersion, typeof(RoslynAnalyzer11.AbstractAnalyzer).Assembly.Location, License.NotRequired /* no dependencies */ );
        }
        
        private string GetLocalNuGetDownloadDir()
        {
            return TestUtils.EnsureTestDirectoryExists(this.TestContext, ".localNuGetDownload");
        }

        private static void AssertSqaleTemplateDoesNotExist(string outputDir)
        {
            string[] matches = Directory.GetFiles(outputDir, "*sqale*template*", SearchOption.AllDirectories);
            Assert.AreEqual(0, matches.Length, "Not expecting any squale template files to exist");
        }

        #endregion

    }
}

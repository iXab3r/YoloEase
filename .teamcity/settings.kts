import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildSteps.dotnetRestore
import jetbrains.buildServer.configs.kotlin.buildSteps.powerShell
import jetbrains.buildServer.configs.kotlin.triggers.vcs

/*
The settings script is an entry point for defining a TeamCity
project hierarchy. The script should contain a single call to the
project() function with a Project instance or an init function as
an argument.

VcsRoots, BuildTypes, Templates, and subprojects can be
registered inside the project using the vcsRoot(), buildType(),
template(), and subProject() methods respectively.

To debug settings scripts in command-line, run the

    mvnDebug org.jetbrains.teamcity:teamcity-configs-maven-plugin:generate

command and attach your debugger to the port 8000.

To debug in IntelliJ Idea, open the 'Maven Projects' tool window (View
-> Tool Windows -> Maven Projects), find the generate task node
(Plugins -> teamcity-configs -> teamcity-configs:generate), the
'Debug' option is available in the context menu for the task.
*/

version = "2025.07"

project {

    buildType(Compile)

    params {
        param("AppVersion", "2.0.%build.number%")
    }
}

object Compile : BuildType({
    templates(AbsoluteId("DotNetTemplate"))
    name = "Compile"

    artifactRules = "Sources/bin.yoloease => YoloEase.%AppVersion%.zip"

    params {
        param("BuildConfiguration", "Release")
        param("BuildOutputPath", "Sources/bin.yoloease")
        text("SolutionPath", "Sources", allowEmpty = false)
        param("DotNetUnitTestsFilter", """"(TestCategory!=Benchmark) & (FullyQualifiedName!~KeyboardLayoutManagerFixture) & (FullyQualifiedName!~CharToKeysConverterFixture) & (FullyQualifiedName!~DxDeviceResolverTests)"""")
        param("BuildRuntime", "win-x64")
        param("ProjectPath", "Sources/YoloEase.UI")
    }

    vcs {
        root(DslContext.settingsRoot)

        cleanCheckout = false
    }

    steps {
        powerShell {
            name = "Init Symlinks"
            id = "RUNNER_32"
            scriptMode = script {
                content = """Start-Process -Wait .\InitSymlinks.cmd"""
            }
        }
        dotnetRestore {
            name = "Restore packages"
            id = "RUNNER_16"
            projects = "%SolutionPath%"
            packagesDir = "%PackagesPath%"
            args = "--ignore-failed-sources"
        }
        stepsOrder = arrayListOf("RUNNER_32", "RUNNER_16", "RUNNER_2", "RUNNER_28", "RUNNER_15")
    }

    triggers {
        vcs {
            id = "vcsTrigger"
            branchFilter = ""
        }
    }

    requirements {
        equals("env.OS", "Windows_NT", "RQ_3")
    }
    
    disableSettings("RUNNER_15", "RUNNER_2")
})

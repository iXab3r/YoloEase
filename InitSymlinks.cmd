@echo off
cd Sources
mklink /J /D PoeShared "../Submodules/PoeEye/PoeEye/PoeShared"
mklink /J /D PoeShared.Wpf "../Submodules/PoeEye/PoeEye/PoeShared.Wpf"
mklink /J /D PoeShared.Native "../Submodules/PoeEye/PoeEye/PoeShared.Native"
mklink /J /D PoeShared.Tests "../Submodules/PoeEye/PoeEye/PoeShared.Tests"
mklink /J /D PoeShared.Squirrel "../Submodules/PoeEye/PoeEye/PoeShared.Squirrel"
mklink /J /D PoeShared.Squirrel.Metadata "../Submodules/PoeEye/PoeEye/PoeShared.Squirrel.Metadata"
mklink /J /D PoeShared.UI "../Submodules/PoeEye/PoeEye/PoeShared.UI"
mklink /J /D PoeShared.Blazor "../Submodules/PoeEye/PoeEye/PoeShared.Blazor"
mklink /J /D PoeShared.Blazor.Controls "../Submodules/PoeEye/PoeEye/PoeShared.Blazor.Controls"
mklink /J /D PoeShared.Blazor.Wpf "../Submodules/PoeEye/PoeEye/PoeShared.Blazor.Wpf"
mklink /J /D PoeShared.Generators "../Submodules/PoeEye/PoeEye/PoeShared.Generators"
mklink /J /D PoeShared.Benchmarks "../Submodules/PoeEye/PoeEye/PoeShared.Benchmarks"
mklink /J /D PoeShared.Launcher "../Submodules/PoeEye/PoeEye/PoeShared.Launcher"
mklink /J /D WindowsHook "../Submodules/PoeEye/PoeEye/WindowsHook"

mklink /J /D PropertyBinder "../Submodules/PropertyBinder/PropertyBinder"
mklink /J /D PropertyBinder.Tests "../Submodules/PropertyBinder/PropertyBinder.Tests"

mklink /J /D gong-wpf-dragdrop "../Submodules/gong-wpf-dragdrop"
mklink /J /D maui "../Submodules/maui"
mklink /J /D MaterialDesignInXamlToolkit "../Submodules/MaterialDesignInXamlToolkit"


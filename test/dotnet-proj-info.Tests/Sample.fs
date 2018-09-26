module DotnetProjInfo.Tests

open System
open System.IO
open Expecto
open Expecto.Logging
open Expecto.Logging.Message
open FileUtils
open Medallion.Shell
open System.IO.Compression
open System.Xml.Linq
open DotnetProjInfo.TestAssets

let RepoDir = (__SOURCE_DIRECTORY__ /".." /"..") |> Path.GetFullPath
let ExamplesDir = RepoDir/"test"/"examples"
let TestRunDir = RepoDir/"test"/"testrun"
let NupkgsDir = RepoDir/"bin"/"nupkg"

let SamplePkgVersion = "1.0.0"
let SamplePkgDir = TestRunDir/"pkgs"/"SamplePkgDir"

let checkExitCodeZero (cmd: Command) =
    Expect.equal 0 cmd.Result.ExitCode "command finished with exit code non-zero."

let prepareTool (fs: FileUtils) pkgUnderTestVersion =
    fs.rm_rf (TestRunDir/"sdk2")
    fs.mkdir_p (TestRunDir/"sdk2")

    fs.cp (RepoDir/"test"/"usetool"/"tools.proj") (TestRunDir/"sdk2")
    fs.createFile (TestRunDir/"sdk2"/"nuget.config") (writeLines 
      [ "<configuration>"
        "  <packageSources>"
        sprintf """    <add key="local" value="%s" />""" NupkgsDir
        "  </packageSources>"
        "</configuration>" ])
    fs.createFile (TestRunDir/"sdk2"/"Directory.Build.props") (writeLines 
      [ """<Project ToolsVersion="15.0">"""
        "  <PropertyGroup>"
        sprintf """    <PkgUnderTestVersion>%s</PkgUnderTestVersion>""" pkgUnderTestVersion
        "  </PropertyGroup>"
        "</Project>" ])

    fs.cd (TestRunDir/"sdk2")
    fs.shellExecRun "dotnet" [ "restore"; "--packages"; "packages" ]
    |> checkExitCodeZero

let projInfo (fs: FileUtils) args =
    fs.cd (TestRunDir/"sdk2")
    fs.shellExecRun "dotnet" ("proj-info" :: args)

let dotnet (fs: FileUtils) args =
    fs.cd (TestRunDir/"sdk2")
    fs.shellExecRun "dotnet" args

let copyDirFromAssets (fs: FileUtils) source outDir =
    fs.mkdir_p outDir

    let path = ExamplesDir/source

    fs.cp_r path outDir
    ()

let tests pkgUnderTestVersion =

  let prepareTestsAssets = lazy(
      let logger = Log.create "Tests Assets"
      let fs = FileUtils(logger)

      // restore tool
      prepareTool fs pkgUnderTestVersion
    )

  let withLog name f test =
    test name (fun () ->
      prepareTestsAssets.Force()

      let logger = Log.create (sprintf "Test '%s'" name)
      let fs = FileUtils(logger)
      f logger fs)

  let withLogAsync name f test =
    test name (async {
      prepareTestsAssets.Force()

      let logger = Log.create (sprintf "Test '%s'" name)
      let fs = FileUtils(logger)
      do! f logger fs })

  let inDir (fs: FileUtils) dirName =
    let outDir = TestRunDir/dirName
    fs.rm_rf outDir
    fs.mkdir_p outDir
    fs.cd outDir
    outDir

  let asLines (s: string) =
    s.Split(Environment.NewLine) |> List.ofArray

  let stdOutLines (cmd: Command) =
    cmd.Result.StandardOutput
    |> fun s -> s.Trim()
    |> asLines

  [ 
    testList "general" [
      testCase |> withLog "can show help" (fun _ fs ->

        projInfo fs ["--help"]
        |> checkExitCodeZero

      )
    ]

    testList ".net" [
      testCase |> withLog "can show installed .net frameworks" (fun _ fs ->

        let result = projInfo fs ["--installed-net-frameworks"]
        result |> checkExitCodeZero
        let out = stdOutLines result

        let isNETVersion (v: string) =
          v.StartsWith("v")
          && v.ToCharArray() |> Array.exists (fun c -> c <> 'v' && c <> '.' <> not (Char.IsNumber c))

        Expect.all out isNETVersion (sprintf "expected a list of .net versions, but was '%A'" out)
      )

      testCase |> withLog "can get .net references path" (fun _ fs ->

        let result = projInfo fs ["--installed-net-frameworks"]
        result |> checkExitCodeZero
        let netFws = stdOutLines result

        for netfw in netFws do
          let result = projInfo fs ["--net-fw-references-path"; "System.Core"; "-f"; netfw]
          result |> checkExitCodeZero
          let out = stdOutLines result
          Expect.exists out (fun s -> s.EndsWith("System.Core.dll")) (sprintf "should resolve System.Core.dll but was '%A'" out)
      )
    ]

    testList "old sdk" [
      testCase |> withLog "can read properties" (fun _ fs ->
        let testDir = inDir fs "oldsdk_props"
        copyDirFromAssets fs ``samples1 OldSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples1 OldSdk library``.ProjectFile)

        let result = projInfo fs [projPath; "--get-property"; "AssemblyName"]
        result |> checkExitCodeZero
        Expect.equal (sprintf "AssemblyName=%s" ``samples1 OldSdk library``.AssemblyName) (result.Result.StandardOutput.Trim()) "wrong output"
      )

      testCase |> withLog "can read fsc args" (fun _ fs ->
        let testDir = inDir fs "oldsdk_fsc_args"
        copyDirFromAssets fs ``samples1 OldSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples1 OldSdk library``.ProjectFile)

        let result = projInfo fs [projPath; "--fsc-args"]
        result |> checkExitCodeZero
      )
    ]

    testList ".net sdk" [
      testCase |> withLog "can read properties" (fun _ fs ->
        let testDir = inDir fs "netsdk_props"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let result = projInfo fs [projPath; "--get-property"; "TargetFramework"]
        result |> checkExitCodeZero
        Expect.equal "TargetFramework=netstandard2.0" (result.Result.StandardOutput.Trim()) "wrong output"
      )

      testCase |> withLog "can read fsc args" (fun _ fs ->
        let testDir = inDir fs "netsdk_fsc_args"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let result = projInfo fs [projPath; "--fsc-args"]
        result |> checkExitCodeZero
      )
    ]

  ]
  |> testList "suite"
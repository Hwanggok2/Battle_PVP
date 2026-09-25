# Mirror 코드 생성 로컬 검사

일반 `dotnet build`는 C# 문법/참조를 검사하지만 Mirror의 Command/RPC 규칙은 실행하지 않는다. 이 도구는 프로젝트에 설치된 `ILPostProcessorHook`을 그대로 사용해 최신 `Assembly-CSharp.dll`의 코드 생성을 검사한다. .NET 10 SDK와 생성된 Unity csproj, 전체 프로젝트 빌드 결과가 필요하다. Unity/플러그인 버전이나 외부 패키지를 내려받지 않는다.

프로젝트 루트에서 순서대로 실행한다.

```powershell
dotnet build Battle_PVP.slnx -m:1 -nodeReuse:false -v:q -p:CustomAfterMicrosoftCommonTargets=C:/Github/Battle_PVP/Tools/Build/AdditionalUnitySources.targets
dotnet build Tools/Tests/MirrorWeaverCheck/MirrorWeaverCheck.csproj -v:q --configfile Tools/Tests/LogicRegression/NuGet.Config
dotnet Tools/Tests/MirrorWeaverCheck/bin/Debug/net10.0/MirrorWeaverCheck.dll
```

다른 작업 디렉터리에서 실행하면 마지막 명령의 끝에 프로젝트 루트의 절대 경로를 인자로 전달한다. Unity 설치 참조는 생성된 `Unity.Mirror.CodeGen.csproj`에서 읽는다.

검사는 전체 런타임 어셈블리의 Weaver 오류가 0인지, 생성된 직렬화 코드와 `CmdUpdateStats` dispatch가 존재하는지 확인한다. 이미 weave된 오래된 어셈블리는 거절한다. 이어서 **메모리 복사본에만** 옛 선택적 매개변수 기본값을 복원해 `CmdUpdateStats cannot have optional parameters` 오류가 재현되는지 확인한다. 프로젝트 원본 DLL/PDB·소스는 덮어쓰지 않는다.

두 단계가 통과하면 종료 코드 0, 진단 오류나 누락된 참조/생성 코드가 있으면 1이다. 이 검사는 Unity의 전체 import/다른 IL postprocessor/플랫폼 빌드, Editor 테스트 실행, 실제 RPC 송수신과 8인 Windows/WebGL 성능 검증을 대체하지 않는다.

`Tools/.gitignore`는 직접 관리하는 검사 프로젝트의 `.csproj`를 버전 관리 대상으로 남기고, 실행 도중 생기는 `bin`/`obj`는 제외한다.

# lib

프로젝트에 직접 포함한 호환성 DLL을 둔다. 현재 `System.Drawing.Common.dll`은 .NET Framework Agent의 이미지 처리 호환성을 위해 사용한다. NuGet으로 복원되는 SQLite DLL은 이 폴더가 아니라 빌드 출력 폴더에 생성되며, 오프라인 설치 프로그램의 Embedded Payload로 별도 포함한다.

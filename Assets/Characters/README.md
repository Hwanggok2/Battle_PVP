# 캐릭터 외형 리소스 등록

로비·대기실·전투 상단의 **캐릭터** 버튼에서 목록과 3D 미리보기를 확인합니다. **적용**은 로비·대기실에서만 가능하며, 선택은 현재 기기의 PlayFab 계정별로 저장됩니다. 다른 기기로의 클라우드 저장은 포함하지 않습니다. 방에서는 서버가 외형 ID를 검증하고 다른 참가자와 나중에 입장한 참가자에게 동기화합니다.

기본 캐릭터와 바바리안(Brute), Megumi, Security Officer, Casual 1, PicoChan이 등록되어 있습니다. Maya와 Emily는 제외했습니다. 새 리소스를 등록하면 목록에 자동으로 나타나므로 UI 코드를 수정할 필요가 없습니다.

## 캐릭터 특성

같은 스탯 배분·직업 보너스를 적용한 기본 캐릭터를 100%로 비교합니다. 서버가 승인한 캐릭터 ID로 능력치를 계산하며, 선택창과 스탯 배분 미리보기에서도 같은 값을 사용합니다. 방어 효율은 기존 상한을 유지합니다. 캐릭터 변경 시 현재 체력 비율을 유지합니다.

| 캐릭터 | 최대 체력 | 방어력 | 공격력 | 이동속도 | 공격속도 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 기본 | 100% | 100% | 100% | 100% | 100% |
| 바바리안 | 120% | 90% | 115% | 90% | 110% |
| Megumi | 90% | 90% | 95% | 108% | 108% |
| Security Officer | 88% | 90% | 95% | 110% | 110% |
| Casual 1 | 90% | 90% | 95% | 108% | 108% |
| PicoChan | 85% | 85% | 90% | 112% | 112% |

각 `Character.asset`의 **Combat Modifiers**에서 초기 밸런스를 조정합니다. 일반적인 작은 체격은 낮은 체력·방어력과 빠른 이동·공격, 큰 체격은 높은 체력·방어력·공격과 느린 이동·공격을 기준으로 정합니다. 바바리안은 낮은 방어력과 빠른 공격의 예외입니다. 검의 공격속도와 활의 차지·발사 복구시간에 적용하며, 활 투사체 속도나 스킬 고유 재사용 시간은 바꾸지 않습니다.

카메라 높이와 거리는 원본 캐릭터의 머리 높이에 비례합니다. 검·활의 표시용 메시만 원본 손뼈에 맞추며 공격 판정/피격 판정은 기존 게임 뼈대를 유지합니다. 미리보기는 선택 시 원본 자세를 한 번 베이크하고 선택·회전할 때만 다시 렌더링합니다.

## 이번에 추가한 리소스

| Id | 이름 | 원본 프리팹/모델 (`Assets/Characters/` 기준) |
| --- | --- | --- |
| brute | Brute | `Brute/Source/BruteBody.prefab` |
| megumi | Megumi | `Megumi/Source/_FBX/Megumi_HightSchoolStudent.fbx` |
| security-officer | Security Officer | `SecurityOfficer/Source/Prefab/Security Officer.prefab` |
| casual-1 | Casual 1 | `Casual1/Source/Casual1/Casual1.prefab` |
| picochan | PicoChan | `PicoChan/Source/Prefabs/PicoChan.prefab` |

각 `<id>/Converted/` 폴더의 `Character.asset`, `NativeVisual.prefab`, `NativeSkin.asset`, URP 재질과 `Portrait.png`를 사용합니다. `CharacterModelConverter.Convert(모델경로, id, 표시이름)`으로 같은 ID를 다시 변환하면 기존 에셋 GUID, 설명, 출처와 썸네일을 유지합니다. 원본 다운로드 묶음은 Git에서 제외한 `CharacterDownloads/`에 보관합니다.

Brute의 원본 도끼는 제외하고 게임의 무기를 사용합니다. 나머지 네 캐릭터는 의상을 포함한 프리팹/모델을 변환했습니다. 원본 데모 씬, 스크립트와 패키지 manifest는 가져오지 않았습니다.

Megumi의 CC BY 4.0 저작자·수정 표시와 출처/라이선스 링크는 선택창에서 확인할 수 있습니다. 배포용 고지는 `Assets/StreamingAssets/CharacterNotices.txt`에 있으며, 원본 Readme도 보관합니다. 다른 캐릭터의 출처와 이용 조건도 같은 위치에 기록했습니다.

## 다른 Humanoid 모델 변환

1. FBX와 의상·재질·텍스처를 가져오고, FBX Rig를 **Humanoid / Create From This Model**로 설정합니다. 프리팹의 의상과 머리가 활성화돼 있는지 확인합니다.
2. Project 창에서 모델/프리팹을 선택하고 **Battle PvP → Characters → Convert Selected Humanoid**를 실행합니다.
3. `Character.asset`의 이름, 설명, Portrait, Attribution, Source Url, License Url, Combat Modifiers를 설정합니다. 새 캐릭터는 모든 배율이 1(100%)입니다. 공개한 Id는 변경하지 않습니다.
4. **Validate Catalog**를 실행하고 대기·이동·앉기·검·활·사망 자세를 확인합니다. 썸네일은 Sprite로 지정하거나 `CharacterPortraitRenderer.Render`로 생성할 수 있습니다.

변환기는 원본 체형, 정점 위치, 스킨 가중치와 뼈대를 유지하며 여러 몸체 메시를 하나로 합칩니다. 같은 재질의 서브메시는 합치고 URP Lit 재질을 만듭니다. 실행 중에는 기존 게임 애니메이터의 Humanoid 자세를 원본 Avatar에 전달합니다. 원본 전용 셰이더의 특수 효과는 그대로 복제하지 않습니다.

추가 머리카락·치마 본도 원본 계층을 유지합니다. 원본의 물리 스크립트와 표정 블렌드셰이프는 포함하지 않습니다. 독특한 체형/의상은 격한 자세에서 관통할 수 있으므로 필요하면 원본의 스킨 가중치를 수정하고 재변환하세요. 피격 판정과 무기는 기존 게임 뼈대를 사용합니다.

## 기존 게임 뼈대와 호환되는 모델을 직접 등록하는 경우

- 기준 모델: `Assets/Player/Anim/Ch10_nonPBR.fbx`.
- 기존 뼈대와 **동일한 바인드 포즈·좌표계·크기**로 스키닝된 모델을 준비하세요. 본 이름의 `mixamorig:` 같은 네임스페이스 차이는 허용합니다.
- 몸·의상·머리는 **하나의 SkinnedMeshRenderer**로 합칩니다. 여러 서브메시와 재질은 지원합니다.
- Unity URP에서 사용할 수 있는 재질을 준비합니다.
- **Register Selected Model**은 이미 기준 뼈대에 맞춘 모델용입니다. 다른 Humanoid 모델은 위의 **Convert Selected Humanoid**를 사용합니다. 등록 도구는 호환되지 않는 본·바인드 포즈·재질을 검출하고 등록을 중단합니다.
- 외형 선택은 게임 애니메이터와 충돌체를 교체하지 않습니다. 카메라·무기 표시는 원본 체형에 맞추고 능력치는 Combat Modifiers를 적용합니다. 피격 범위는 기본 캐릭터와 공유합니다.

## 새 모델 등록 순서

1. FBX와 재질을 `Assets/Characters/` 아래 원하는 폴더로 가져옵니다.
2. Project 창에서 모델 FBX 또는 모델 프리팹을 선택합니다.
3. Unity 메뉴 **Battle PvP → Characters → Register Selected Model**을 실행합니다.
4. 자동 생성된 `Character.asset`의 **Display Name**, **Description**, **Portrait**를 설정합니다. 썸네일 이미지는 Texture Type을 `Sprite (2D and UI)`로 가져옵니다.
5. 필요하면 **Materials**에 재질을 지정합니다. 비워 두면 모델 원본 재질을 사용합니다. 지정할 때는 서브메시 수만큼 모두 채웁니다.
6. **Battle PvP → Characters → Validate Catalog**로 확인한 뒤 로비에서 선택·미리보기·적용을 확인합니다.
7. 호스트와 참가자가 같은 캐릭터 목록과 리소스를 포함하도록 클라이언트를 다시 빌드합니다.

등록 도구는 `Assets/Resources/CharacterCatalog.asset`의 Characters 목록에 자동으로 추가합니다. 배열 순서는 표시 순서일 뿐이며 저장/네트워크에는 고유 **Id**를 사용합니다. 배포한 캐릭터의 Id는 변경하거나 중복 사용하지 마세요. 누락된 ID는 기본 외형으로 표시됩니다.

## 기본 모델의 재질만 다른 외형

Project 창에서 **Create → Battle PvP → Characters → Character**를 선택합니다. 고유 Id와 이름을 넣고 **Use Default Body**를 켠 뒤 Materials에 기본 메시의 모든 재질을 지정합니다. 카탈로그 Characters에 추가하고 Validate Catalog를 실행합니다. Body는 비워 둡니다.

## 연결 파일

- 카탈로그: `Assets/Resources/CharacterCatalog.asset`
- 기본 외형: `Assets/Characters/DefaultCharacter.asset`
- 선택 UI: `Assets/Prefabs/CharacterSelection.prefab`
- 실제 적용: `Assets/Prefabs/Player.prefab`의 `PlayerAppearance` → Body
- 초기 설치/복구: **Battle PvP → Characters → Install Character Selection**. 씬을 저장하고 Play Mode를 종료한 상태에서 실행합니다. 기존 카탈로그는 유지합니다.

새 외형 추가 후에는 이동·앉기·검·활·스킬·은신·버프·사망·부활과 두 클라이언트 간 외형 표시를 확인하세요. 원본 외형은 별도의 렌더러로 교체하므로 다른 정점 형식의 메시를 기존 Ch10 렌더러에 넣지 않습니다. 피격 판정이나 전투 상태를 초기화하지 않습니다.

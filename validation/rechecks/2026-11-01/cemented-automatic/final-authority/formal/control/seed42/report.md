
# 自动初始结构设计报告

Automatic cemented structures

此报告展示已保存证据，不重新计算或改变验收结论。
一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。
方案库快照只供查看和导出；精确续跑请打开原检查点并使用原计算版本。

## 来源与兼容性

- **运行记录**: cemented-seed42
- **证据指纹**: ea18f6f45ab97368e05a22e483ebb3ad3490be3f31088e2283f56d3b70ef6fc6
- **算法身份**: AutonomousTopologyV1
- **搜索计划指纹**: 9754bbceffee687e72a05a54c167fdffd0672057b826f9ed042f86dfbaee84d7
- **版本 / 修订**: 1 / 28
- **计算版本**: 未记录
- **状态**: Completed

## 原验收规格

- **目标焦距 / F数**: 50 mm / F/12.5
- **焦距 / F数相对容差**: 0.02 / 0.02
- **RMS / 最大半径上限**: 50 / 150 µm
- **畸变 / 有效率**: 0.03 / 0.98
- **中心与边界检查**: 沿用记录的原验收协议，丢失光线不得视为合格

全部原目标、制造限制、采样、权限和预算如下，未作放宽。

```json
{
  "originalPreparationPlan": null,
  "searchOrSourcePlan": {
    "seeds": [
      {
        "schemaVersion": 1,
        "algorithm": "AutonomousTopologyV1",
        "name": "SPC existing authored singlet",
        "conjugate": "infinite",
        "reference": {
          "id": "reference",
          "zMillimeters": 0
        },
        "stop": {
          "id": "stop",
          "zMillimeters": 1,
          "clearSemiDiameterMillimeters": 2,
          "mechanicalSemiDiameterMillimeters": null
        },
        "image": {
          "id": "image",
          "zMillimeters": 55,
          "protected": true
        },
        "elements": [
          {
            "id": "crown",
            "frontSurfaceId": "front",
            "backSurfaceId": "back",
            "frontZMillimeters": 3,
            "thicknessMillimeters": 3,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": 0.014326902586484919,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": null
            },
            "back": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.0247344692910669,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": null
            },
            "protected": false,
            "transition": false
          }
        ],
        "fields": [
          {
            "label": "Axis",
            "xAngleDegrees": 0,
            "yAngleDegrees": 0,
            "weight": 1
          },
          {
            "label": "Edge",
            "xAngleDegrees": 0,
            "yAngleDegrees": 3,
            "weight": 1
          }
        ],
        "wavelengths": [
          {
            "label": "F",
            "nanometers": 486.1,
            "weight": 1,
            "isPrimary": false
          },
          {
            "label": "d",
            "nanometers": 587.6,
            "weight": 1,
            "isPrimary": true
          },
          {
            "label": "C",
            "nanometers": 656.3,
            "weight": 1,
            "isPrimary": false
          }
        ],
        "glassCatalogs": [],
        "rayAimingEnabled": true,
        "matchRefractiveIndexData": true,
        "temperatureCelsius": 20,
        "pressureAtmospheres": 1
      }
    ],
    "targets": {
      "effectiveFocalLengthMillimeters": 50,
      "fNumber": 12.5,
      "focalLengthRelativeTolerance": 0.02,
      "fNumberRelativeTolerance": 0.02,
      "maximumRmsRadiusMillimeters": 0.05,
      "maximumSpotRadiusMillimeters": 0.15,
      "maximumDistortionFraction": 0.03,
      "minimumValidRayFraction": 0.98,
      "minimumEdgeThicknessMillimeters": 0.1,
      "minimumEdgeAirGapMillimeters": 0.01,
      "maximumOpticalTrackMillimeters": 10000,
      "maximumAbsoluteSurfaceSlope": 10
    },
    "constraints": {
      "minimumElementCount": 1,
      "maximumElementCount": 8,
      "minimumCenterThicknessMillimeters": 0.1,
      "minimumAirGapMillimeters": 0.05,
      "minimumBackFocusMillimeters": 0.1,
      "maximumMechanicalTrackMillimeters": 10000,
      "allowTransitionElements": false,
      "allowedGlassNames": []
    },
    "options": {
      "expansion": {
        "enableInsertion": true,
        "enableDeletion": false,
        "enableGradualDeletion": false,
        "enableSplit": false,
        "enableMerge": false,
        "enableMaterial": true,
        "enableProfile": false,
        "materialAlternatives": [
          "N-BK7",
          "N-F2"
        ],
        "deletionScales": [
          0.7,
          0.4,
          0.2,
          0.1,
          0.02
        ],
        "directDeletionRecoveryMaximumFunctionEvaluations": null,
        "splitAirGapMillimeters": 0.05,
        "splitFirstFraction": 0.55,
        "maximumEvenPower": 8,
        "maximumAbsoluteConic": 2,
        "coefficientCoordinateSagMillimeters": 0.02
      },
      "axialCompensation": {
        "maximumDisplacementMillimeters": 0.25,
        "coordinateScaleMillimeters": 0.1,
        "elementIds": []
      },
      "maximumTrials": 3,
      "candidatesPerElementCount": 4,
      "branchesPerParent": 4,
      "maximumFunctionEvaluations": 300,
      "maximumGradientEvaluations": 200,
      "maximumTracedRays": 3000000,
      "maximumStageTracedRays": 500000,
      "finalValidationRayReserve": 100000,
      "timeLimit": "00:02:00",
      "seed": 42,
      "curvatureBoundPerMillimeter": 0.08,
      "insertionMaterials": [
        "N-F2"
      ],
      "recovery": {
        "maximumAcceptedSteps": 30,
        "maximumFunctionEvaluations": 24,
        "timeLimit": "00:00:10",
        "aScale": 0.01,
        "focalCScale": 0.01,
        "aTolerance": 0.0001,
        "focalCTolerance": 0.0001,
        "minimumChangeWeight": 1E-06,
        "relativeDifferenceStep": 6.055454452393343E-06,
        "relativeRankTolerance": 1E-08,
        "profileRadialSamples": 64
      },
      "local": {
        "maximumUpdates": 3,
        "maximumFunctionEvaluations": 20,
        "maximumGradientEvaluations": 20,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "includeBoundaryClearance": true,
          "includeBoundaryMaximum": true,
          "fieldsPerDirection": 3,
          "pupilRings": 2,
          "pupilArms": 8,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      },
      "full": {
        "maximumUpdates": 4,
        "maximumFunctionEvaluations": 20,
        "maximumGradientEvaluations": 20,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "includeBoundaryClearance": true,
          "includeBoundaryMaximum": true,
          "fieldsPerDirection": 3,
          "pupilRings": 2,
          "pupilArms": 8,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      }
    }
  }
}
```


## 预算与实际后端

准备和主搜索费用分列，阶段费用已包含在累计费用中，不得重复相加；未记录不代表零工作。

### 主搜索累计费用


```json
{
  "consumed": {
    "functionEvaluations": 82,
    "gradientEvaluations": 27,
    "validationEvaluations": 4,
    "matrixEvaluations": 51,
    "differenceEvaluations": 36,
    "jacobianBuilds": 9,
    "tracedRays": 202218,
    "geometryProbes": 25675,
    "elapsed": "00:00:08.5537460"
  },
  "reservation": null,
  "options": {
    "expansion": {
      "enableInsertion": true,
      "enableDeletion": false,
      "enableGradualDeletion": false,
      "enableSplit": false,
      "enableMerge": false,
      "enableMaterial": true,
      "enableProfile": false,
      "materialAlternatives": [
        "N-BK7",
        "N-F2"
      ],
      "deletionScales": [
        0.7,
        0.4,
        0.2,
        0.1,
        0.02
      ],
      "directDeletionRecoveryMaximumFunctionEvaluations": null,
      "splitAirGapMillimeters": 0.05,
      "splitFirstFraction": 0.55,
      "maximumEvenPower": 8,
      "maximumAbsoluteConic": 2,
      "coefficientCoordinateSagMillimeters": 0.02
    },
    "axialCompensation": {
      "maximumDisplacementMillimeters": 0.25,
      "coordinateScaleMillimeters": 0.1,
      "elementIds": []
    },
    "maximumTrials": 3,
    "candidatesPerElementCount": 4,
    "branchesPerParent": 4,
    "maximumFunctionEvaluations": 300,
    "maximumGradientEvaluations": 200,
    "maximumTracedRays": 3000000,
    "maximumStageTracedRays": 500000,
    "finalValidationRayReserve": 100000,
    "timeLimit": "00:02:00",
    "seed": 42,
    "curvatureBoundPerMillimeter": 0.08,
    "insertionMaterials": [
      "N-F2"
    ],
    "recovery": {
      "maximumAcceptedSteps": 30,
      "maximumFunctionEvaluations": 24,
      "timeLimit": "00:00:10",
      "aScale": 0.01,
      "focalCScale": 0.01,
      "aTolerance": 0.0001,
      "focalCTolerance": 0.0001,
      "minimumChangeWeight": 1E-06,
      "relativeDifferenceStep": 6.055454452393343E-06,
      "relativeRankTolerance": 1E-08,
      "profileRadialSamples": 64
    },
    "local": {
      "maximumUpdates": 3,
      "maximumFunctionEvaluations": 20,
      "maximumGradientEvaluations": 20,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "includeBoundaryClearance": true,
        "includeBoundaryMaximum": true,
        "fieldsPerDirection": 3,
        "pupilRings": 2,
        "pupilArms": 8,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    },
    "full": {
      "maximumUpdates": 4,
      "maximumFunctionEvaluations": 20,
      "maximumGradientEvaluations": 20,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "includeBoundaryClearance": true,
        "includeBoundaryMaximum": true,
        "fieldsPerDirection": 3,
        "pupilRings": 2,
        "pupilArms": 8,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    }
  }
}
```

实际设备只按已记录的执行证据报告，不能用所选设备代替实际后端。
- **trial-0**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0
- **trial-0**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0
- **trial-1**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0
- **trial-1**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0
- **trial-2**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0
- **trial-2**: cpu-forward-v1; device=CPU; fallback=unrecorded; GPU derivative rays=0; batches=0

## 候选与原规格验收


### main:seed-0

- **来源**: 自主搜索
- **父方案**: —
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 1
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.014326902586484919 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.0247344692910669 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.00084374164106 mm / 12.500210935410266

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.325173721104159 | 17.272629327926932 | 960/960 | 99/99 | 0 |
| 2 | 9.32432547262097 | 17.518416234562544 | 960/960 | 99/99 | 7.452164663607723E-05 |
| 3 | 9.321860103650234 | 17.80472011279016 | 960/960 | 99/99 | 0.000298091215564025 |
| 4 | 9.31801597233987 | 18.131564417954603 | 960/960 | 99/99 | 0.0006707225947215162 |
| 5 | 9.313191058230005 | 18.49897511102949 | 960/960 | 99/99 | 0.0011924389321718231 |
| 6 | 9.30794387270168 | 18.90698066211838 | 960/960 | 99/99 | 0.0018632726387781359 |
| 7 | 9.302994403108482 | 19.355612054081984 | 960/960 | 99/99 | 0.0026832653918792706 |
| 8 | 9.299224753320157 | 19.84490278648432 | 960/960 | 99/99 | 0.003652468139794715 |
| 9 | 9.297679041125672 | 20.374888879677755 | 960/960 | 99/99 | 0.0047709411074347815 |
| 10 | 9.299562012963493 | 20.945608879162634 | 960/960 | 99/99 | 0.006038753802936173 |
| 11 | 9.306235746949811 | 21.55710386014098 | 960/960 | 99/99 | 0.0074559850254504 |
| 12 | 9.319213748773947 | 22.209417432329957 | 960/960 | 99/99 | 0.009022722873905107 |
| 13 | 9.340151718886103 | 22.902595744983543 | 960/960 | 99/99 | 0.0107390647563145 |
| 14 | 9.370834304114636 | 23.63668749208592 | 960/960 | 99/99 | 0.01260511740112602 |
| 15 | 9.413157263978222 | 24.411743917875704 | 960/960 | 99/99 | 0.014620996868542303 |
| 16 | 9.469104699526996 | 25.227818822523673 | 960/960 | 99/99 | 0.016786828563627024 |
| 17 | 9.54072131869156 | 26.08496856804221 | 960/960 | 99/99 | 0.01910274725005208 |
| 18 | 9.630080138200531 | 26.983252084456844 | 960/960 | 99/99 | 0.021568897064901678 |
| 19 | 9.739246516529942 | 27.922730876171364 | 960/960 | 99/99 | 0.024185431534894615 |
| 20 | 9.8702399186539 | 28.903469028569884 | 960/960 | 99/99 | 0.026952513593129158 |
| 21 | 10.02499525476311 | 29.925533214896216 | 960/960 | 99/99 | 0.02987031559690126 |

### main:trial-0

- **来源**: 自主搜索
- **父方案**: seed-0
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 1
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3.00020891184499 | N-F2 | Standard | 5 | 未记录 | 0.012784850259399806 | 0 |  |
| back | 6.000700098288414 | Air | Standard | 5 | 未记录 | -0.019822260303649416 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.906226066374415 mm / 12.476556516593604

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 16.39575214053628 | 36.80981866088606 | 960/960 | 99/99 | 0 |
| 2 | 16.398413602324776 | 36.98436547611442 | 960/960 | 99/99 | 7.244634334240707E-05 |
| 3 | 16.406442208444954 | 37.200385331539906 | 960/960 | 99/99 | 0.00028978987345065315 |
| 4 | 16.419970417575996 | 37.45789905079461 | 960/960 | 99/99 | 0.0006520440911332438 |
| 5 | 16.439218309153734 | 37.756930077044835 | 960/960 | 99/99 | 0.0011592314994233945 |
| 6 | 16.464492533697246 | 38.09750447643956 | 960/960 | 99/99 | 0.001811383605854212 |
| 7 | 16.496184800198602 | 38.47965094177608 | 960/960 | 99/99 | 0.0026085409265994754 |
| 8 | 16.534769860291025 | 38.90340079640331 | 960/960 | 99/99 | 0.003550752990624673 |
| 9 | 16.580802941444546 | 39.368787998392605 | 960/960 | 99/99 | 0.004638078345317624 |
| 10 | 16.63491657704833 | 39.87584914494202 | 960/960 | 99/99 | 0.005870584563111706 |
| 11 | 16.69781678065845 | 40.42462347697962 | 960/960 | 99/99 | 0.0072483482492755845 |
| 12 | 16.770278515565835 | 41.015152884115615 | 960/960 | 99/99 | 0.008771455050396091 |
| 13 | 16.853140419726657 | 41.64748190973411 | 960/960 | 99/99 | 0.010439999664105158 |
| 14 | 16.947298760366458 | 42.32165775635788 | 960/960 | 99/99 | 0.01225408584981508 |
| 15 | 17.053700612283134 | 43.03773029133539 | 960/960 | 99/99 | 0.014213826440389758 |
| 16 | 17.17333627878561 | 43.79575205265374 | 960/960 | 99/99 | 0.016319343355011746 |
| 17 | 17.307231003541823 | 44.59577825509831 | 960/960 | 99/99 | 0.01857076761280864 |
| 18 | 17.45643605418395 | 45.43786679660444 | 960/960 | 99/99 | 0.020968239347537086 |
| 19 | 17.622019292566513 | 46.32207826489232 | 960/960 | 99/99 | 0.023511907824207597 |
| 20 | 17.805055379947955 | 47.24847594432413 | 960/960 | 99/99 | 0.026201931454706808 |
| 21 | 18.0066157955876 | 48.21712582303039 | 960/960 | 99/99 | 0.02903847781685136 |

### main:trial-1

- **来源**: 自主搜索
- **父方案**: trial-0
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 1
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3.000713326148295 | N-BK7 | Standard | 5 | 未记录 | 0.014387900854133965 | 0 |  |
| back | 6.00024300816499 | Air | Standard | 5 | 未记录 | -0.024801096477244027 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.839652604163874 mm / 12.459913151040968

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.657339649982866 | 23.847072246413767 | 960/960 | 99/99 | 0 |
| 2 | 9.661539406496997 | 24.094214573282592 | 960/960 | 99/99 | 7.4730319485585E-05 |
| 3 | 9.674205351338319 | 24.381986440875803 | 960/960 | 99/99 | 0.0002989259186933648 |
| 4 | 9.695536658414113 | 24.710411405436727 | 960/960 | 99/99 | 0.0006726007206402656 |
| 5 | 9.725862433543476 | 25.079515533424022 | 960/960 | 99/99 | 0.0011957779321474043 |
| 6 | 9.765637403697331 | 25.48932740503296 | 960/960 | 99/99 | 0.0018684900461438834 |
| 7 | 9.815435822613054 | 25.93987811783771 | 960/960 | 99/99 | 0.002690778845470016 |
| 8 | 9.875943556393104 | 26.431201290717297 | 960/960 | 99/99 | 0.003662695407471152 |
| 9 | 9.947948333287833 | 26.9633330679897 | 960/960 | 99/99 | 0.004784300109679241 |
| 10 | 10.032328182334007 | 27.536312123742192 | 960/960 | 99/99 | 0.006055662636201706 |
| 11 | 10.13003814629263 | 28.150179666435182 | 960/960 | 99/99 | 0.007476861985733154 |
| 12 | 10.242095433157953 | 28.80497944368954 | 960/960 | 99/99 | 0.009047986480089549 |
| 13 | 10.36956326222189 | 29.500757747316754 | 960/960 | 99/99 | 0.010769133774032946 |
| 14 | 10.513533757178296 | 30.23756341856498 | 960/960 | 99/99 | 0.012640410866129667 |
| 15 | 10.675110329535606 | 31.015447853598666 | 960/960 | 99/99 | 0.014661934110182934 |
| 16 | 10.855390069024898 | 31.83446500922571 | 960/960 | 99/99 | 0.01683382922864315 |
| 17 | 11.05544670238332 | 32.69467140877147 | 960/960 | 99/99 | 0.01915623132614947 |
| 18 | 11.276314688952636 | 33.59612614829955 | 960/960 | 99/99 | 0.02162928490428868 |
| 19 | 11.51897498604205 | 34.53889090296158 | 960/960 | 99/99 | 0.024253143878087177 |
| 20 | 11.784342939478938 | 35.52302993365242 | 960/960 | 99/99 | 0.02702797159247911 |
| 21 | 12.073258641338755 | 36.54861009378685 | 960/960 | 99/99 | 0.029953940840667635 |

### main:trial-2

- **来源**: 自主搜索
- **父方案**: trial-1
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 1
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3.0005699391946976 | N-F2 | Standard | 5 | 未记录 | 0.012691891783062683 | 0 |  |
| back | 6.001016979314285 | Air | Standard | 5 | 未记录 | -0.019727024590125316 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.19307857036635 mm / 12.548269642591588

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 16.89885462427677 | 29.028253490982422 | 960/960 | 99/99 | 0 |
| 2 | 16.89631313404748 | 29.3825718462917 | 960/960 | 99/99 | 7.210879911909955E-05 |
| 3 | 16.888731119494885 | 29.69661493307213 | 960/960 | 99/99 | 0.00028843967728487915 |
| 4 | 16.876236151976475 | 29.97040766862108 | 960/960 | 99/99 | 0.0006490060777669256 |
| 5 | 16.85904152110134 | 30.203972414913572 | 960/960 | 99/99 | 0.0011538304077131987 |
| 6 | 16.837447220074527 | 30.397328982112892 | 960/960 | 99/99 | 0.001802944040519272 |
| 7 | 16.811841282789455 | 30.55049463180182 | 960/960 | 99/99 | 0.0025963873195621844 |
| 8 | 16.782701427883012 | 30.663484080032056 | 960/960 | 99/99 | 0.003534209562742765 |
| 9 | 16.750596948834854 | 30.73630950008677 | 960/960 | 99/99 | 0.004616469068026971 |
| 10 | 16.716190770712082 | 30.768980525041066 | 960/960 | 99/99 | 0.00584323311997524 |
| 11 | 16.68024157304349 | 30.761504250083505 | 960/960 | 99/99 | 0.007214577997486252 |
| 12 | 16.643605854442235 | 30.713885234576743 | 960/960 | 99/99 | 0.00873058898230014 |
| 13 | 16.607239788162516 | 30.62612550391175 | 960/960 | 99/99 | 0.010391360368824975 |
| 14 | 16.57220068933129 | 30.52700719414947 | 960/960 | 99/99 | 0.01219699547461687 |
| 15 | 16.53964788520117 | 31.238150626460957 | 960/960 | 99/99 | 0.014147606651961412 |
| 16 | 16.510842751148225 | 31.991030447779735 | 960/960 | 99/99 | 0.016243315301104004 |
| 17 | 16.48714764972388 | 32.78570155838656 | 960/960 | 99/99 | 0.01848425188347392 |
| 18 | 16.470023491143976 | 33.62222153201478 | 960/960 | 99/99 | 0.02087055593656935 |
| 19 | 16.461025625241717 | 34.500650622402596 | 960/960 | 99/99 | 0.023402376090102497 |
| 20 | 16.461797781825204 | 35.421051770193834 | 960/960 | 99/99 | 0.0260798700822027 |
| 21 | 16.474063803597737 | 36.38349060998847 | 960/960 | 99/99 | 0.028903204777986045 |

## 阶段与淘汰原因


### seed-0 · Validation

- **父方案**: —
- **结果代码**: Accepted [Accepted]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 325,
    "elapsed": "00:00:00.9864407"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-0
- **结果代码**: Recovered [Recovered]

```json
{
  "work": {
    "functionEvaluations": 17,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 17,
    "differenceEvaluations": 12,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 5200,
    "elapsed": "00:00:00.0780398"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Local

- **父方案**: seed-0
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 4,
    "gradientEvaluations": 4,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 3576,
    "geometryProbes": 1300,
    "elapsed": "00:00:00.3474845"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Full

- **父方案**: seed-0
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 5,
    "gradientEvaluations": 5,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 4470,
    "geometryProbes": 1625,
    "elapsed": "00:00:00.4626722"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Validation

- **父方案**: seed-0
- **结果代码**: Accepted [Accepted]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 325,
    "elapsed": "00:00:00.3295223"
  },
  "knownFunctionEvaluations": null
}
```


### trial-1 · Recovery

- **父方案**: trial-0
- **结果代码**: Recovered [Recovered]

```json
{
  "work": {
    "functionEvaluations": 17,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 17,
    "differenceEvaluations": 12,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 5200,
    "elapsed": "00:00:00.0365275"
  },
  "knownFunctionEvaluations": null
}
```


### trial-1 · Local

- **父方案**: trial-0
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 4,
    "gradientEvaluations": 4,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 3576,
    "geometryProbes": 1300,
    "elapsed": "00:00:00.4703306"
  },
  "knownFunctionEvaluations": null
}
```


### trial-1 · Full

- **父方案**: trial-0
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 5,
    "gradientEvaluations": 5,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 4470,
    "geometryProbes": 1625,
    "elapsed": "00:00:00.7940063"
  },
  "knownFunctionEvaluations": null
}
```


### trial-1 · Validation

- **父方案**: trial-0
- **结果代码**: Accepted [Accepted]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 325,
    "elapsed": "00:00:01.5468056"
  },
  "knownFunctionEvaluations": null
}
```


### trial-2 · Recovery

- **父方案**: trial-1
- **结果代码**: Recovered [Recovered]

```json
{
  "work": {
    "functionEvaluations": 17,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 17,
    "differenceEvaluations": 12,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 5200,
    "elapsed": "00:00:00.0950488"
  },
  "knownFunctionEvaluations": null
}
```


### trial-2 · Local

- **父方案**: trial-1
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 4,
    "gradientEvaluations": 4,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 3576,
    "geometryProbes": 1300,
    "elapsed": "00:00:00.6913901"
  },
  "knownFunctionEvaluations": null
}
```


### trial-2 · Full

- **父方案**: trial-1
- **结果代码**: UpdateLimit [UpdateLimit]

```json
{
  "work": {
    "functionEvaluations": 5,
    "gradientEvaluations": 5,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 4470,
    "geometryProbes": 1625,
    "elapsed": "00:00:00.7088129"
  },
  "knownFunctionEvaluations": null
}
```


### trial-2 · Validation

- **父方案**: trial-1
- **结果代码**: Accepted [Accepted]

```json
{
  "work": {
    "functionEvaluations": 1,
    "gradientEvaluations": 0,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 44520,
    "geometryProbes": 325,
    "elapsed": "00:00:00.7258013"
  },
  "knownFunctionEvaluations": null
}
```


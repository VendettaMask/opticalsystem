
# 自动初始结构设计报告

Automatic cemented structures

此报告展示已保存证据，不重新计算或改变验收结论。
一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。
方案库快照只供查看和导出；精确续跑请打开原检查点并使用原计算版本。

## 来源与兼容性

- **运行记录**: cemented-seed43
- **证据指纹**: 7b9e0dbe4e6427c0e908c0ffb8dc61bd3f0e3face80d9353c02289f104d9ab7b
- **算法身份**: AutonomousTopologyV1
- **搜索计划指纹**: 2092433eb74103f5baaa9871cf3e0cf383f765f45a7f9e99cacf2083800a92c4
- **版本 / 修订**: 1 / 37
- **计算版本**: b19abadf3c4de1de2a0b23d03bc521ca7b7d331b1ca9c23640c470285c45f1d9
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
        "cemented": {
          "version": 1,
          "maximumGroupElements": 2,
          "initialThicknessMillimeters": 1,
          "thicknessMultipliers": [
            1
          ]
        },
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
      "seed": 43,
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
    "functionEvaluations": 101,
    "gradientEvaluations": 27,
    "validationEvaluations": 4,
    "matrixEvaluations": 70,
    "differenceEvaluations": 55,
    "jacobianBuilds": 9,
    "tracedRays": 202218,
    "geometryProbes": 50571,
    "elapsed": "00:00:06.8067918"
  },
  "reservation": null,
  "options": {
    "expansion": {
      "cemented": {
        "version": 1,
        "maximumGroupElements": 2,
        "initialThicknessMillimeters": 1,
        "thicknessMultipliers": [
          1
        ]
      },
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
    "seed": 43,
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

- **来源**: 自主胶合
- **父方案**: seed-0
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 2.998643458431557 | N-BK7 | Standard | 5 | 未记录 | 0.02703501757950745 | 0 |  |
| back | 6.001015359696918 | N-F2 | Standard | 5 | 未记录 | -0.025439563542622425 | 0 |  |
| trial-0-cemented:back | 6.999196816891892 | Air | Standard | 5 | 未记录 | -0.014424444115605634 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.87224543050009 mm / 12.468061357625022

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 6.738271760620257 | 13.310155869101141 | 960/960 | 99/99 | 0 |
| 2 | 6.737904807640395 | 13.381519939353826 | 960/960 | 99/99 | 7.344967899073466E-05 |
| 3 | 6.736940554257016 | 13.498038909501686 | 960/960 | 99/99 | 0.00029380352010004716 |
| 4 | 6.735789043186949 | 13.65972838196894 | 960/960 | 99/99 | 0.000661075936295882 |
| 5 | 6.735134238699116 | 13.866607211993621 | 960/960 | 99/99 | 0.0011752909515440554 |
| 6 | 6.7359341863753865 | 14.11869751116579 | 960/960 | 99/99 | 0.0018364822027778002 |
| 7 | 6.739419874870574 | 14.41602465130465 | 960/960 | 99/99 | 0.002644692945383426 |
| 8 | 6.747091446931947 | 14.75861726873806 | 960/960 | 99/99 | 0.0035999760574380113 |
| 9 | 6.760710070043712 | 15.14650726896516 | 960/960 | 99/99 | 0.004702394046598272 |
| 10 | 6.7822835390843395 | 15.579729831667555 | 960/960 | 99/99 | 0.005952019057850871 |
| 11 | 6.814043626857085 | 16.058323416114906 | 960/960 | 99/99 | 0.007348932882310274 |
| 12 | 6.858413423913173 | 16.58232976698981 | 960/960 | 99/99 | 0.008893226967202267 |
| 13 | 6.91796351349507 | 17.151793920536342 | 960/960 | 99/99 | 0.01058500242736147 |
| 14 | 6.9953568658821785 | 17.76676421110568 | 960/960 | 99/99 | 0.012424370057278773 |
| 15 | 7.093283774668935 | 18.42729227812523 | 960/960 | 99/99 | 0.014411450345140565 |
| 16 | 7.21438983054865 | 19.133433073423856 | 960/960 | 99/99 | 0.016546373487172365 |
| 17 | 7.3612015320592885 | 19.885244868918406 | 960/960 | 99/99 | 0.018829279404477445 |
| 18 | 7.536055274398444 | 20.68278926474809 | 960/960 | 99/99 | 0.021260317759116496 |
| 19 | 7.74103576643673 | 21.526131197755483 | 960/960 | 99/99 | 0.02383964797372569 |
| 20 | 7.977929196458901 | 22.415338950379393 | 960/960 | 99/99 | 0.026567439250225224 |
| 21 | 8.248194764768414 | 23.350484159914586 | 960/960 | 99/99 | 0.02944387059114213 |

### main:trial-1

- **来源**: 自主胶合
- **父方案**: trial-0
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3.001456166833045 | N-F2 | Standard | 5 | 未记录 | 0.022685449372912513 | 0 |  |
| back | 6.001015359696746 | N-F2 | Standard | 5 | 未记录 | -0.025439563542613577 | 0 |  |
| trial-0-cemented:back | 6.999264677736192 | Air | Standard | 5 | 未记录 | -0.01005897350162611 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.78423680604194 mm / 12.446059201510485

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 16.8042820889414 | 38.97686152749692 | 960/960 | 99/99 | 0 |
| 2 | 16.808262910547224 | 39.00412286441981 | 960/960 | 99/99 | 7.050994009221326E-05 |
| 3 | 16.82025320399489 | 39.07636160382189 | 960/960 | 99/99 | 0.0002820443015328689 |
| 4 | 16.840396131049225 | 39.19359154619057 | 960/960 | 99/99 | 0.0006346167082391104 |
| 5 | 16.868929209133928 | 39.35582968518392 | 960/960 | 99/99 | 0.001128249868625346 |
| 6 | 16.906182665697727 | 39.56309621106635 | 960/960 | 99/99 | 0.0017629755782822825 |
| 7 | 16.952577101393544 | 39.81541451442483 | 960/960 | 99/99 | 0.002538834724165692 |
| 8 | 17.00862043324167 | 40.1128111903174 | 960/960 | 99/99 | 0.0034558772894564384 |
| 9 | 17.074904086504528 | 40.45531604272523 | 960/960 | 99/99 | 0.004514162359879204 |
| 10 | 17.15209840567843 | 40.84296208943394 | 960/960 | 99/99 | 0.00571375813068488 |
| 11 | 17.240947261508985 | 41.27578556723055 | 960/960 | 99/99 | 0.007054741915657978 |
| 12 | 17.342261842667785 | 41.75382593751631 | 960/960 | 99/99 | 0.008537200156077148 |
| 13 | 17.45691363784658 | 42.27712589224764 | 960/960 | 99/99 | 0.010161228431877837 |
| 14 | 17.58582663630085 | 42.845731360267926 | 960/960 | 99/99 | 0.011926931473263545 |
| 15 | 17.729968801607715 | 43.45969151402862 | 960/960 | 99/99 | 0.013834423173589608 |
| 16 | 17.890342903452364 | 44.11905877664335 | 960/960 | 99/99 | 0.015883826603771127 |
| 17 | 18.06797682395243 | 44.82388882936617 | 960/960 | 99/99 | 0.018075274027358784 |
| 18 | 18.263913486310887 | 45.574240619420614 | 960/960 | 99/99 | 0.020408906916479137 |
| 19 | 18.47920058207544 | 46.37017636816543 | 960/960 | 99/99 | 0.022884875970422307 |
| 20 | 18.71488029651343 | 47.21176157974227 | 960/960 | 99/99 | 0.02550334113335039 |
| 21 | 18.971979247245415 | 48.09906505000549 | 960/960 | 99/99 | 0.028264471614462314 |

### main:trial-2

- **来源**: 自主胶合
- **父方案**: trial-1
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 2.9990467093122435 | N-BK7 | Standard | 5 | 未记录 | 0.026926675795431024 | 0 |  |
| back | 6.000965391167295 | N-F2 | Standard | 5 | 未记录 | -0.02480158277166538 | 0 |  |
| trial-0-cemented:back | 6.999671749547126 | Air | Standard | 5 | 未记录 | -0.014248787001956325 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.11325294248518 mm / 12.528313235621296

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.837962479771985 | 18.998206616274746 | 960/960 | 99/99 | 0 |
| 2 | 9.829793317778854 | 19.13143664017078 | 960/960 | 99/99 | 7.308932778713621E-05 |
| 3 | 9.805337644897074 | 19.220202953107503 | 960/960 | 99/99 | 0.0002923620932098867 |
| 4 | 9.764752227011783 | 19.264516616232964 | 960/960 | 99/99 | 0.0006578326428262122 |
| 5 | 9.708302804939379 | 19.26438548627818 | 960/960 | 99/99 | 0.001169524889593635 |
| 6 | 9.636370939335565 | 19.219814217738108 | 960/960 | 99/99 | 0.0018274723157211679 |
| 7 | 9.549463890349568 | 19.130804264702704 | 960/960 | 99/99 | 0.0026317179767549916 |
| 8 | 9.448227827234328 | 18.99882071596363 | 960/960 | 99/99 | 0.0035823145072151983 |
| 9 | 9.333464740792234 | 18.841988593679105 | 960/960 | 99/99 | 0.0046793241265464655 |
| 10 | 9.206153495451334 | 18.663020386899486 | 960/960 | 99/99 | 0.005922818647189668 |
| 11 | 9.067475492760588 | 18.462918979446037 | 960/960 | 99/99 | 0.007312879483297576 |
| 12 | 8.918845398475943 | 18.24126355161539 | 960/960 | 99/99 | 0.00884959766060054 |
| 13 | 8.761947269679048 | 18.004054830888276 | 960/960 | 99/99 | 0.010533073827943933 |
| 14 | 8.598776142918583 | 17.739239336600523 | 960/960 | 99/99 | 0.012363418269503164 |
| 15 | 8.43168461702616 | 17.447022419481765 | 960/960 | 99/99 | 0.014340750918246528 |
| 16 | 8.263433060998167 | 17.143647522367708 | 960/960 | 99/99 | 0.01646520137106881 |
| 17 | 8.097240649609246 | 16.82572942841803 | 960/960 | 99/99 | 0.018736908904388807 |
| 18 | 7.936832335745018 | 16.484010331458027 | 960/960 | 99/99 | 0.021156022491454006 |
| 19 | 7.786474051720223 | 16.1186762464837 | 960/960 | 99/99 | 0.023722700820933276 |
| 20 | 7.6509850660940835 | 15.72995237470868 | 960/960 | 99/99 | 0.026437112316113783 |
| 21 | 7.5357131350730775 | 15.31811171596964 | 960/960 | 99/99 | 0.029299435156111976 |

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
    "elapsed": "00:00:00.9565047"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-0
- **结果代码**: NotDispatched [NotDispatched]
- **原因**: Known zero work; reservation refunded and pending phase retained.

```json
{
  "work": {
    "functionEvaluations": 0,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 0,
    "geometryProbes": 0,
    "elapsed": "00:00:00"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-0
- **结果代码**: BudgetExhausted [BudgetExhausted]
- **原因**: First-order result is not acceptance; retain its charged evidence and continue original-gate physical compensation.

```json
{
  "work": {
    "functionEvaluations": 24,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 24,
    "differenceEvaluations": 19,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 11914,
    "elapsed": "00:00:00.1749820"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Local

- **父方案**: seed-0
- **结果代码**: Cancelled [Cancelled]
- **原因**: Same-template Adam coordinates, moments, learning rate and residuals retained.

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 2,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 1788,
    "geometryProbes": 1036,
    "elapsed": "00:00:00.3721469"
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
    "functionEvaluations": 2,
    "gradientEvaluations": 2,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 1788,
    "geometryProbes": 1036,
    "elapsed": "00:00:00.4198746"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Full

- **父方案**: seed-0
- **结果代码**: Cancelled [Cancelled]
- **原因**: Same-template Adam coordinates, moments, learning rate and residuals retained.

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 2,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 1788,
    "geometryProbes": 1036,
    "elapsed": "00:00:00.2361324"
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
    "functionEvaluations": 3,
    "gradientEvaluations": 3,
    "validationEvaluations": 0,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 2682,
    "geometryProbes": 1554,
    "elapsed": "00:00:00.4806785"
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
    "geometryProbes": 518,
    "elapsed": "00:00:00.4483627"
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
    "functionEvaluations": 23,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 23,
    "differenceEvaluations": 18,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 11396,
    "elapsed": "00:00:00.1306159"
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
    "geometryProbes": 2072,
    "elapsed": "00:00:00.5465093"
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
    "geometryProbes": 2590,
    "elapsed": "00:00:00.3701224"
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
    "geometryProbes": 518,
    "elapsed": "00:00:00.3029141"
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
    "functionEvaluations": 23,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 23,
    "differenceEvaluations": 18,
    "jacobianBuilds": 3,
    "tracedRays": 0,
    "geometryProbes": 11396,
    "elapsed": "00:00:00.0545971"
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
    "geometryProbes": 2072,
    "elapsed": "00:00:00.1512288"
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
    "geometryProbes": 2590,
    "elapsed": "00:00:00.2838580"
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
    "geometryProbes": 518,
    "elapsed": "00:00:00.5154565"
  },
  "knownFunctionEvaluations": null
}
```


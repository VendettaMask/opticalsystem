
# 自动初始结构设计报告

Automatic cemented structures

此报告展示已保存证据，不重新计算或改变验收结论。
一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。
方案库快照只供查看和导出；精确续跑请打开原检查点并使用原计算版本。

## 来源与兼容性

- **运行记录**: cemented-seed42
- **证据指纹**: 5634bacc3368a055a0953cad8d153a4c71050f6e7576dc952bb7b4853eba8422
- **算法身份**: AutonomousTopologyV1
- **搜索计划指纹**: 7b4b5bb27be44fef30a068f718ad8aa87acadea0173e75d5ca824af4b60fd091
- **版本 / 修订**: 1 / 12
- **计算版本**: 335a44ebf1677b9558f48afc4a136aacfe77ffc8ec7987985130e8036607b69f
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
            "frontZMillimeters": 2.998643458431557,
            "thicknessMillimeters": 3.002371901265361,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": 0.02703501757950745,
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
                "curvaturePerMillimeter": -0.025439563542622425,
                "conic": 0,
                "maximumEvenPower": 0,
                "terms": []
              },
              "clearSemiDiameterMillimeters": 5,
              "mechanicalSemiDiameterMillimeters": null
            },
            "protected": false,
            "transition": false
          },
          {
            "id": "trial-0-cemented",
            "frontSurfaceId": "back",
            "backSurfaceId": "trial-0-cemented:back",
            "frontZMillimeters": 6.001015359696918,
            "thicknessMillimeters": 0.9981814571949741,
            "material": "N-F2",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": -0.025439563542622425,
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
                "curvaturePerMillimeter": -0.014424444115605634,
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
        "cementedInterfaces": {
          "version": 1,
          "interfaces": [
            {
              "leftElementId": "crown",
              "rightElementId": "trial-0-cemented",
              "surfaceId": "back"
            }
          ]
        },
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
          "maximumGroupElements": 3,
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
      "maximumTrials": 1,
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
    "functionEvaluations": 35,
    "gradientEvaluations": 9,
    "validationEvaluations": 2,
    "matrixEvaluations": 24,
    "differenceEvaluations": 20,
    "jacobianBuilds": 2,
    "tracedRays": 97086,
    "geometryProbes": 23981,
    "elapsed": "00:00:04.3838356"
  },
  "reservation": null,
  "options": {
    "expansion": {
      "cemented": {
        "version": 1,
        "maximumGroupElements": 3,
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
    "maximumTrials": 1,
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

## 候选与原规格验收


### main:seed-0

- **来源**: 自主胶合
- **父方案**: —
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

### main:trial-0

- **来源**: 自主胶合
- **父方案**: seed-0
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 3
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 2.9916663136463435 | N-BK7 | Standard | 5 | 未记录 | 0.03415291064681822 | 0 |  |
| back | 6.000979235384123 | N-F2 | Standard | 5 | 未记录 | -0.025626922097111163 | 0 |  |
| trial-0-cemented:back | 6.999196816891914 | N-F2 | Standard | 5 | 未记录 | -0.01442444411559202 | 0 |  |
| trial-0-cemented-1:back | 7.998764058308642 | Air | Standard | 5 | 未记录 | -0.00833126864910833 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.036153998502094 mm / 12.509038499625523

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 8.951094452032615 | 17.258592373515643 | 960/960 | 99/99 | 0 |
| 2 | 8.941342543532476 | 17.26512883281431 | 960/960 | 99/99 | 7.135389914193438E-05 |
| 3 | 8.912137581030835 | 17.232537399226626 | 960/960 | 99/99 | 0.00028542038177738297 |
| 4 | 8.863633565673052 | 17.177632766341734 | 960/960 | 99/99 | 0.00064221380360099 |
| 5 | 8.79609295789029 | 17.099150277519872 | 960/960 | 99/99 | 0.001141758093143733 |
| 6 | 8.709895665690809 | 17.001276097418952 | 960/960 | 99/99 | 0.00178408675438305 |
| 7 | 8.605552243734234 | 16.88083318412747 | 960/960 | 99/99 | 0.0025692428719785903 |
| 8 | 8.483721951217808 | 16.737350056050932 | 960/960 | 99/99 | 0.003497279116277352 |
| 9 | 8.345236544430385 | 16.57081197007569 | 960/960 | 99/99 | 0.004568257750603719 |
| 10 | 8.19113093026338 | 16.38120685928696 | 960/960 | 99/99 | 0.005782250639183192 |
| 11 | 8.022682068194497 | 16.168525451524875 | 960/960 | 99/99 | 0.007139339257355655 |
| 12 | 7.841457747299028 | 15.93276147905968 | 960/960 | 99/99 | 0.008639614701425869 |
| 13 | 7.649377009559703 | 15.680030148019709 | 960/960 | 99/99 | 0.01028317770140968 |
| 14 | 7.448783900377701 | 15.40600134155471 | 960/960 | 99/99 | 0.01207013863426561 |
| 15 | 7.2425356472866405 | 15.11006726545721 | 960/960 | 99/99 | 0.014000617538173403 |
| 16 | 7.034104870296252 | 14.792236324584202 | 960/960 | 99/99 | 0.016074744128813004 |
| 17 | 6.827692351875816 | 14.452519672023644 | 960/960 | 99/99 | 0.01829265781642351 |
| 18 | 6.628341350443488 | 14.090931967545739 | 960/960 | 99/99 | 0.020654507724147685 |
| 19 | 6.442035510331065 | 13.707492348808486 | 960/960 | 99/99 | 0.02316045270803542 |
| 20 | 6.275749829219322 | 13.30222568681602 | 960/960 | 99/99 | 0.025810661377986457 |
| 21 | 6.1374097179000255 | 12.875164226023553 | 960/960 | 99/99 | 0.028605312120179006 |

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
    "geometryProbes": 518,
    "elapsed": "00:00:01.1622787"
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
    "differenceEvaluations": 20,
    "jacobianBuilds": 2,
    "tracedRays": 0,
    "geometryProbes": 16353,
    "elapsed": "00:00:00.1591368"
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
    "geometryProbes": 2844,
    "elapsed": "00:00:00.7721700"
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
    "geometryProbes": 3555,
    "elapsed": "00:00:00.9429784"
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
    "geometryProbes": 711,
    "elapsed": "00:00:01.0106272"
  },
  "knownFunctionEvaluations": null
}
```


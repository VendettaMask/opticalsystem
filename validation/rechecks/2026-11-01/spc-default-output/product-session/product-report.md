
# 自动初始结构设计报告

SPC automatic preparation and continuation

此报告展示已保存证据，不重新计算或改变验收结论。
一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。
方案库快照只供查看和导出；精确续跑请打开原检查点并使用原计算版本。

## 来源与兼容性

- **运行记录**: autonomous-b4f2f0606d0c4be0b9f11eac1cd5ac33
- **证据指纹**: 7bc5b8b57f7ecbf515dd4c6c3cd4de3daeca7f249ac6ddbc94641f2bb612bad4
- **算法身份**: AutonomousTopologyV1
- **准备模式**: Spc
- **原任务指纹**: 4a48fefccf61d66ea0b70fb4f3b57102222e7845e57a3a14cb21b02aafb54eed
- **搜索计划指纹**: 17a7ee4e80b729b09394dd5b5d5c4b67984f3e0846ed3fd5ab1650745765cde0
- **版本 / 修订**: 1 / 10
- **计算版本**: 2d2600fb32c86efd3f256de43f9991737521e718632cbfe6556347ea8ce1d22d
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
  "originalPreparationPlan": {
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
                "curvaturePerMillimeter": 0.025,
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
                "curvaturePerMillimeter": -0.03333333333333333,
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
      "seedPreparation": {
        "spc": {
          "version": 1,
          "maximumParents": 1,
          "maximumGradientEvaluations": 600,
          "parentIterations": 80,
          "branchIterations": 40,
          "physicalUpdatesPerStage": 12,
          "maximumMaterialAlternatives": 2,
          "gradientTolerance": 1E-07,
          "curvatureDifferenceStep": 0.001,
          "branchCoordinateStep": 0.05,
          "thicknessMultipliers": [
            1,
            2,
            4
          ]
        },
        "version": 1,
        "maximumStarts": 12,
        "retainedSeeds": 4,
        "maximumFunctionEvaluations": 600,
        "maximumEvaluationsPerStart": 40,
        "timeLimit": "00:02:00",
        "maximumTracedRays": 2000000,
        "focusATolerance": 0.001
      },
      "expansion": {
        "enableInsertion": true,
        "enableDeletion": true,
        "enableGradualDeletion": true,
        "enableSplit": true,
        "enableMerge": true,
        "enableMaterial": true,
        "enableProfile": true,
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
      "maximumTrials": 1,
      "candidatesPerElementCount": 4,
      "branchesPerParent": 6,
      "maximumFunctionEvaluations": 400,
      "maximumGradientEvaluations": 100,
      "maximumTracedRays": 600000,
      "maximumStageTracedRays": 200000,
      "finalValidationRayReserve": 100000,
      "timeLimit": "00:00:30",
      "seed": 42,
      "curvatureBoundPerMillimeter": 0.08,
      "insertionMaterials": [
        "N-BK7",
        "N-F2"
      ],
      "recovery": {
        "maximumAcceptedSteps": 30,
        "maximumFunctionEvaluations": 400,
        "timeLimit": "00:01:00",
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
        "gateCompensationVersion": 4,
        "maximumUpdates": 80,
        "maximumFunctionEvaluations": 82,
        "maximumGradientEvaluations": 81,
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
        "maximumUpdates": 160,
        "maximumFunctionEvaluations": 161,
        "maximumGradientEvaluations": 161,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "fieldsPerDirection": 3,
          "pupilRings": 3,
          "pupilArms": 12,
          "profileRadialSamples": 64
        },
        "performFinalAssessment": false
      }
    }
  },
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
      },
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
                "curvaturePerMillimeter": 0.022806858048807294,
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
                "curvaturePerMillimeter": -0.015870339412355345,
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
            "id": "parent-0-spc",
            "frontSurfaceId": "parent-0-spc:front",
            "backSurfaceId": "parent-0-spc:back",
            "frontZMillimeters": 6.050000000000001,
            "thicknessMillimeters": 0.10000000000000053,
            "material": "N-BK7",
            "front": {
              "profile": {
                "kind": "standard",
                "curvaturePerMillimeter": 0.019505365489213847,
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
                "curvaturePerMillimeter": 0.019505383717758642,
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
      "seedPreparation": {
        "spc": {
          "version": 1,
          "maximumParents": 1,
          "maximumGradientEvaluations": 600,
          "parentIterations": 80,
          "branchIterations": 40,
          "physicalUpdatesPerStage": 12,
          "maximumMaterialAlternatives": 2,
          "gradientTolerance": 1E-07,
          "curvatureDifferenceStep": 0.001,
          "branchCoordinateStep": 0.05,
          "thicknessMultipliers": [
            1,
            2,
            4
          ]
        },
        "version": 1,
        "maximumStarts": 12,
        "retainedSeeds": 4,
        "maximumFunctionEvaluations": 600,
        "maximumEvaluationsPerStart": 40,
        "timeLimit": "00:02:00",
        "maximumTracedRays": 2000000,
        "focusATolerance": 0.001
      },
      "expansion": {
        "enableInsertion": true,
        "enableDeletion": true,
        "enableGradualDeletion": true,
        "enableSplit": true,
        "enableMerge": true,
        "enableMaterial": true,
        "enableProfile": true,
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
      "maximumTrials": 1,
      "candidatesPerElementCount": 4,
      "branchesPerParent": 6,
      "maximumFunctionEvaluations": 400,
      "maximumGradientEvaluations": 100,
      "maximumTracedRays": 600000,
      "maximumStageTracedRays": 200000,
      "finalValidationRayReserve": 100000,
      "timeLimit": "00:00:30",
      "seed": 42,
      "curvatureBoundPerMillimeter": 0.08,
      "insertionMaterials": [
        "N-BK7",
        "N-F2"
      ],
      "recovery": {
        "maximumAcceptedSteps": 30,
        "maximumFunctionEvaluations": 400,
        "timeLimit": "00:01:00",
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
        "gateCompensationVersion": 4,
        "maximumUpdates": 80,
        "maximumFunctionEvaluations": 82,
        "maximumGradientEvaluations": 81,
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
        "maximumUpdates": 160,
        "maximumFunctionEvaluations": 161,
        "maximumGradientEvaluations": 161,
        "timeLimit": "00:05:00",
        "learningRate": 0.01,
        "computeMode": "cpu",
        "sampling": {
          "fieldsPerDirection": 3,
          "pupilRings": 3,
          "pupilArms": 12,
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
    "functionEvaluations": 162,
    "gradientEvaluations": 0,
    "validationEvaluations": 2,
    "matrixEvaluations": 68,
    "differenceEvaluations": 88,
    "jacobianBuilds": 22,
    "tracedRays": 89040,
    "geometryProbes": 42459,
    "elapsed": "00:00:00.6065160"
  },
  "reservation": null,
  "options": {
    "seedPreparation": {
      "spc": {
        "version": 1,
        "maximumParents": 1,
        "maximumGradientEvaluations": 600,
        "parentIterations": 80,
        "branchIterations": 40,
        "physicalUpdatesPerStage": 12,
        "maximumMaterialAlternatives": 2,
        "gradientTolerance": 1E-07,
        "curvatureDifferenceStep": 0.001,
        "branchCoordinateStep": 0.05,
        "thicknessMultipliers": [
          1,
          2,
          4
        ]
      },
      "version": 1,
      "maximumStarts": 12,
      "retainedSeeds": 4,
      "maximumFunctionEvaluations": 600,
      "maximumEvaluationsPerStart": 40,
      "timeLimit": "00:02:00",
      "maximumTracedRays": 2000000,
      "focusATolerance": 0.001
    },
    "expansion": {
      "enableInsertion": true,
      "enableDeletion": true,
      "enableGradualDeletion": true,
      "enableSplit": true,
      "enableMerge": true,
      "enableMaterial": true,
      "enableProfile": true,
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
    "maximumTrials": 1,
    "candidatesPerElementCount": 4,
    "branchesPerParent": 6,
    "maximumFunctionEvaluations": 400,
    "maximumGradientEvaluations": 100,
    "maximumTracedRays": 600000,
    "maximumStageTracedRays": 200000,
    "finalValidationRayReserve": 100000,
    "timeLimit": "00:00:30",
    "seed": 42,
    "curvatureBoundPerMillimeter": 0.08,
    "insertionMaterials": [
      "N-BK7",
      "N-F2"
    ],
    "recovery": {
      "maximumAcceptedSteps": 30,
      "maximumFunctionEvaluations": 400,
      "timeLimit": "00:01:00",
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
      "gateCompensationVersion": 4,
      "maximumUpdates": 80,
      "maximumFunctionEvaluations": 82,
      "maximumGradientEvaluations": 81,
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
      "maximumUpdates": 160,
      "maximumFunctionEvaluations": 161,
      "maximumGradientEvaluations": 161,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "fieldsPerDirection": 3,
        "pupilRings": 3,
        "pupilArms": 12,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    }
  }
}
```


### 准备累计费用


```json
{
  "consumed": {
    "functionEvaluations": 161,
    "gradientEvaluations": 151,
    "validationEvaluations": 10,
    "matrixEvaluations": 0,
    "differenceEvaluations": 56,
    "jacobianBuilds": 0,
    "tracedRays": 503394,
    "geometryProbes": 18723,
    "elapsed": "00:00:07.6748103"
  },
  "options": {
    "spc": {
      "version": 1,
      "maximumParents": 1,
      "maximumGradientEvaluations": 600,
      "parentIterations": 80,
      "branchIterations": 40,
      "physicalUpdatesPerStage": 12,
      "maximumMaterialAlternatives": 2,
      "gradientTolerance": 1E-07,
      "curvatureDifferenceStep": 0.001,
      "branchCoordinateStep": 0.05,
      "thicknessMultipliers": [
        1,
        2,
        4
      ]
    },
    "version": 1,
    "maximumStarts": 12,
    "retainedSeeds": 4,
    "maximumFunctionEvaluations": 600,
    "maximumEvaluationsPerStart": 40,
    "timeLimit": "00:02:00",
    "maximumTracedRays": 2000000,
    "focusATolerance": 0.001
  },
  "pendingPhase": null,
  "reservation": null,
  "inFlight": null,
  "persistenceElapsed": "00:00:03.4221925",
  "lostReservations": 0
}
```

实际设备只按已记录的执行证据报告，不能用所选设备代替实际后端。
未记录

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

### main:seed-1

- **来源**: 自主搜索
- **父方案**: —
- **验收**: 原规格合格
- **结果代码**: 保留候选 [保留候选]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.022806858048807294 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.015870339412355345 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | 0.019505365489213847 | 0 |  |
| parent-0-spc:back | 6.150000000000001 | Air | Standard | 5 | 未记录 | 0.019505383717758642 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.46223554457982 mm / 12.615558886144955

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.678377323336402 | 16.68892279665415 | 960/960 | 99/99 | 0 |
| 2 | 9.674921087629123 | 16.89206648797359 | 960/960 | 99/99 | 6.807280955992105E-05 |
| 3 | 9.664636257614706 | 17.051489992422454 | 960/960 | 99/99 | 0.00027229577910398855 |
| 4 | 9.64777538420452 | 17.167209448911148 | 960/960 | 99/99 | 0.000612682532006626 |
| 5 | 9.624762417315502 | 17.239237911815696 | 960/960 | 99/99 | 0.0010892557760531674 |
| 6 | 9.596197131586424 | 17.267585353742064 | 960/960 | 99/99 | 0.00170204730573711 |
| 7 | 9.562861062804306 | 17.252258667939667 | 960/960 | 99/99 | 0.002451098006461284 |
| 8 | 9.525724658780934 | 17.1932616703927 | 960/960 | 99/99 | 0.00333645785988026 |
| 9 | 9.485955212325685 | 17.090595101523 | 960/960 | 99/99 | 0.0043581859496012515 |
| 10 | 9.444924972050085 | 16.94425662759724 | 960/960 | 99/99 | 0.005516350468781437 |
| 11 | 9.404218616374417 | 17.267399177620433 | 960/960 | 99/99 | 0.006811028728516832 |
| 12 | 9.365639031022004 | 17.83842591248508 | 960/960 | 99/99 | 0.008242307167602936 |
| 13 | 9.331210064533732 | 18.454111610853417 | 960/960 | 99/99 | 0.009810281362824046 |
| 14 | 9.30317467882068 | 19.114506295726752 | 960/960 | 99/99 | 0.011515056041277541 |
| 15 | 9.283986710588506 | 19.819663169696298 | 960/960 | 99/99 | 0.013356745093138353 |
| 16 | 9.276294383698096 | 20.569638622248476 | 960/960 | 99/99 | 0.015335471585756178 |
| 17 | 9.282913848356515 | 21.36449223741499 | 960/960 | 99/99 | 0.01745136777899134 |
| 18 | 9.306791460588117 | 22.204286801826623 | 960/960 | 99/99 | 0.019704575141598235 |
| 19 | 9.350954323283142 | 23.08908831307921 | 960/960 | 99/99 | 0.022095244368857958 |
| 20 | 9.418449799249423 | 24.018965988513497 | 960/960 | 99/99 | 0.024623535401290243 |
| 21 | 9.512276192200355 | 24.99399227432919 | 960/960 | 99/99 | 0.027289617444264773 |

### spc:parent-0:original

- **来源**: 已有父方案
- **父方案**: —
- **验收**: 原规格未达标
- **结果代码**: IndexOneSaddleCertified [IndexOneSaddleCertified]
- **片数**: 1
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.025 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.03333333333333333 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 33.66281259865559 mm / 8.415703149663898

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 693.3404156028536 | 1012.4232859779265 | 960/960 | 99/99 | 0 |
| 2 | 693.3553697759179 | 1012.873598684999 | 960/960 | 99/99 | 0.00010325260707974834 |
| 3 | 693.4002331911474 | 1013.3814010289301 | 960/960 | 99/99 | 0.0004130165841320099 |
| 4 | 693.4750085366347 | 1013.9467311432034 | 960/960 | 99/99 | 0.0009293103994030032 |
| 5 | 693.579700292475 | 1014.5696303573715 | 960/960 | 99/99 | 0.0016521648351967228 |
| 6 | 693.7143147306924 | 1015.2501432017842 | 960/960 | 99/99 | 0.0025816229911189825 |
| 7 | 693.8788599151205 | 1015.9883174125138 | 960/960 | 99/99 | 0.003717740288073117 |
| 8 | 694.0733457012601 | 1016.784203936504 | 960/960 | 99/99 | 0.005060584473925629 |
| 9 | 694.2977837361034 | 1017.6378569369009 | 960/960 | 99/99 | 0.006610235630335795 |
| 10 | 694.5521874579226 | 1018.5493337986164 | 960/960 | 99/99 | 0.008366786180150406 |
| 11 | 694.8365720960356 | 1019.5186951340864 | 960/960 | 99/99 | 0.010330340897031123 |
| 12 | 695.1509546705427 | 1020.5460047892565 | 960/960 | 99/99 | 0.012501016915578539 |
| 13 | 695.4953539920208 | 1021.6313298497586 | 960/960 | 99/99 | 0.01487894374285579 |
| 14 | 695.8697906612133 | 1022.774740647295 | 960/960 | 99/99 | 0.017464263271245235 |
| 15 | 696.2742870686722 | 1023.976310766269 | 960/960 | 99/99 | 0.020257129792513377 |
| 16 | 696.7088673943858 | 1025.2361170505412 | 960/960 | 99/99 | 0.023257710012900702 |
| 17 | 697.1735576073825 | 1026.5542396105407 | 960/960 | 99/99 | 0.026466183069646515 |
| 18 | 697.6683854653055 | 1027.930761830448 | 960/960 | 99/99 | 0.029882740548637286 |
| 19 | 698.1933805139735 | 1029.3657703756205 | 960/960 | 99/99 | 0.03350758650325622 |
| 20 | 698.748574086914 | 1030.859355200307 | 960/960 | 99/99 | 0.03734093747493781 |
| 21 | 699.3339993048868 | 1032.4116095554823 | 960/960 | 99/99 | 0.041383022513859974 |
- **focal-length**: 0.3267437480268882 / 0.02
- **f-number**: 0.3267437480268882 / 0.02
- **field:0:rms**: 0.6933404156028535 / 0.05
- **field:0:maximum**: 1.0124232859779265 / 0.15
- **field:1:rms**: 0.6933553697759179 / 0.05
- **field:1:maximum**: 1.012873598684999 / 0.15
- **field:2:rms**: 0.6934002331911474 / 0.05
- **field:2:maximum**: 1.0133814010289302 / 0.15
- **field:3:rms**: 0.6934750085366347 / 0.05
- **field:3:maximum**: 1.0139467311432033 / 0.15
- **field:4:rms**: 0.693579700292475 / 0.05
- **field:4:maximum**: 1.0145696303573715 / 0.15
- **field:5:rms**: 0.6937143147306924 / 0.05
- **field:5:maximum**: 1.0152501432017842 / 0.15
- **field:6:rms**: 0.6938788599151205 / 0.05
- **field:6:maximum**: 1.0159883174125137 / 0.15
- **field:7:rms**: 0.6940733457012601 / 0.05
- **field:7:maximum**: 1.016784203936504 / 0.15
- **field:8:rms**: 0.6942977837361034 / 0.05
- **field:8:maximum**: 1.017637856936901 / 0.15
- **field:9:rms**: 0.6945521874579226 / 0.05
- **field:9:maximum**: 1.0185493337986165 / 0.15
- **field:10:rms**: 0.6948365720960357 / 0.05
- **field:10:maximum**: 1.0195186951340864 / 0.15
- **field:11:rms**: 0.6951509546705427 / 0.05
- **field:11:maximum**: 1.0205460047892565 / 0.15
- **field:12:rms**: 0.6954953539920208 / 0.05
- **field:12:maximum**: 1.0216313298497586 / 0.15
- **field:13:rms**: 0.6958697906612133 / 0.05
- **field:13:maximum**: 1.022774740647295 / 0.15
- **field:14:rms**: 0.6962742870686721 / 0.05
- **field:14:maximum**: 1.023976310766269 / 0.15
- **field:15:rms**: 0.6967088673943859 / 0.05
- **field:15:maximum**: 1.0252361170505413 / 0.15
- **field:16:rms**: 0.6971735576073824 / 0.05
- **field:16:maximum**: 1.0265542396105407 / 0.15
- **field:17:rms**: 0.6976683854653055 / 0.05
- **field:17:maximum**: 1.027930761830448 / 0.15
- **field:18:rms**: 0.6981933805139735 / 0.05
- **field:18:maximum**: 1.0293657703756205 / 0.15
- **field:19:rms**: 0.698748574086914 / 0.05
- **field:19:maximum**: 1.030859355200307 / 0.15
- **field:20:rms**: 0.6993339993048868 / 0.05
- **field:20:maximum**: 1.0324116095554823 / 0.15

### spc:parent-0:stationary

- **来源**: SPC父结构优化
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: IndexOneSaddleCertified [IndexOneSaddleCertified]
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

### spc:parent-0:minus:stage-0

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.010010706502289825 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.04176744280915449 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | -0.04485431060513014 | 0 |  |
| parent-0-spc:back | 6.150000000000001 | Air | Standard | 5 | 未记录 | -0.0323394933189567 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.64965990506949 mm / 12.412414976267373

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.22729296403823 | 20.157532359433628 | 960/960 | 99/99 | 0 |
| 2 | 9.22886475066311 | 20.45469673693226 | 960/960 | 99/99 | 7.869938415132158E-05 |
| 3 | 9.233652615425623 | 20.790599368052774 | 960/960 | 99/99 | 0.0003148022369741045 |
| 4 | 9.241873713324564 | 21.165265651163278 | 960/960 | 99/99 | 0.000708322659689758 |
| 5 | 9.253888734457849 | 21.578723199318418 | 960/960 | 99/99 | 0.0012592841558461381 |
| 6 | 9.27019992739567 | 22.03100184356743 | 960/960 | 99/99 | 0.0019677196341327226 |
| 7 | 9.291448090082344 | 22.522133636295184 | 960/960 | 99/99 | 0.002833671411284389 |
| 8 | 9.31840830288865 | 23.052152854821763 | 960/960 | 99/99 | 0.0038571912166972732 |
| 9 | 9.351984139237583 | 23.621096005097986 | 960/960 | 99/99 | 0.005038340197983616 |
| 10 | 9.393200071443625 | 24.22900182560994 | 960/960 | 99/99 | 0.0063771889266924 |
| 11 | 9.443191799603806 | 24.87591129140676 | 960/960 | 99/99 | 0.00787381740642332 |
| 12 | 9.50319427580661 | 25.561867618362434 | 960/960 | 99/99 | 0.009528315080303697 |
| 13 | 9.574527279361725 | 26.286916267503013 | 960/960 | 99/99 | 0.011340780840846392 |
| 14 | 9.658578522784676 | 27.05110494956009 | 960/960 | 99/99 | 0.01331132303973351 |
| 15 | 9.756784429660566 | 27.854483629715876 | 960/960 | 99/99 | 0.015440059499411421 |
| 16 | 9.870608914760227 | 28.69710453245622 | 960/960 | 99/99 | 0.01772711752492313 |
| 17 | 10.001520697420622 | 29.57902214657615 | 960/960 | 99/99 | 0.020172633917375334 |
| 18 | 10.150969868756135 | 30.50029323043768 | 960/960 | 99/99 | 0.02277675498795238 |
| 19 | 10.320364585624278 | 31.460976817331066 | 960/960 | 99/99 | 0.02553963657295681 |
| 20 | 10.511048853905098 | 32.46113422097174 | 960/960 | 99/99 | 0.02846144405020621 |
| 21 | 10.724282370761511 | 33.50082904127616 | 960/960 | 99/99 | 0.03154235235580789 |

### spc:parent-0:minus:stage-1

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.010010706502289825 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.04176744280915449 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | -0.04485431060513014 | 0 |  |
| parent-0-spc:back | 6.250000000000001 | Air | Standard | 5 | 未记录 | -0.0323394933189567 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.659508868175536 mm / 12.414877217043884

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.577043726591992 | 16.151778846383067 | 960/960 | 99/99 | 0 |
| 2 | 9.575616688797764 | 16.445075102982127 | 960/960 | 99/99 | 7.96755881844632E-05 |
| 3 | 9.571404681838752 | 16.776829368240985 | 960/960 | 99/99 | 0.0003187070762744741 |
| 4 | 9.564615344592113 | 17.147066590964933 | 960/960 | 99/99 | 0.0007171086349204885 |
| 5 | 9.555595774645727 | 17.555813902887852 | 960/960 | 99/99 | 0.0012749038834758335 |
| 6 | 9.54483396216135 | 18.003100621896472 | 960/960 | 99/99 | 0.0019921258926570377 |
| 7 | 9.532960562004428 | 18.488958255263977 | 960/960 | 99/99 | 0.002868817187567556 |
| 8 | 9.520750758857764 | 19.01342050316801 | 960/960 | 99/99 | 0.0039050297521911526 |
| 9 | 9.50912589776796 | 19.57652326228998 | 960/960 | 99/99 | 0.005100825034988582 |
| 10 | 9.499154465169982 | 20.178304629602238 | 960/960 | 99/99 | 0.006456273954515326 |
| 11 | 9.492051916573207 | 20.818804906283404 | 960/960 | 99/99 | 0.00797145690765516 |
| 12 | 9.489178763855929 | 21.49806660187803 | 960/960 | 99/99 | 0.009646463776861892 |
| 13 | 9.492036268559907 | 22.216134438491018 | 960/960 | 99/99 | 0.011481393940064586 |
| 14 | 9.502259053013589 | 22.97305535522454 | 960/960 | 99/99 | 0.013476356280564284 |
| 15 | 9.52160395745061 | 23.768878512784973 | 960/960 | 99/99 | 0.015631469198216865 |
| 16 | 9.551934559239069 | 24.603655298213845 | 960/960 | 99/99 | 0.017946860621714043 |
| 17 | 9.59520094861196 | 25.477439329714358 | 960/960 | 99/99 | 0.02042266802146974 |
| 18 | 9.653414635438251 | 26.390286461842827 | 960/960 | 99/99 | 0.023059038423828688 |
| 19 | 9.728618841275305 | 27.34225479060237 | 960/960 | 99/99 | 0.025856128426230314 |
| 20 | 9.82285488789142 | 28.333404658918315 | 960/960 | 99/99 | 0.028814104213086597 |
| 21 | 9.938125881924988 | 29.36379866213068 | 960/960 | 99/99 | 0.03193314157293071 |

### spc:parent-0:minus:stage-2

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.010010706502289825 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.04176744280915449 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | -0.04485431060513014 | 0 |  |
| parent-0-spc:back | 6.450000000000001 | Air | Standard | 5 | 未记录 | -0.0323394933189567 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 49.67921852142554 mm / 12.419804630356385

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 12.32427381798888 | 22.63961935903547 | 960/960 | 99/99 | 0 |
| 2 | 12.318685503166419 | 23.031458460749583 | 960/960 | 99/99 | 8.160991745741432E-05 |
| 3 | 12.301958395927064 | 23.38594074034178 | 960/960 | 99/99 | 0.00032644443788055805 |
| 4 | 12.274206536275303 | 23.703091614250326 | 960/960 | 99/99 | 0.0007345178654858149 |
| 5 | 12.235621757099707 | 23.982934408043 | 960/960 | 99/99 | 0.0013058540423037876 |
| 6 | 12.186476351001264 | 24.225490359428782 | 960/960 | 99/99 | 0.002040486350615526 |
| 7 | 12.127126837040235 | 24.430778621141624 | 960/960 | 99/99 | 0.002938457716060659 |
| 8 | 12.058018856085639 | 24.598816263637026 | 960/960 | 99/99 | 0.0039998206122330475 |
| 9 | 11.979693224549939 | 24.729618277673502 | 960/960 | 99/99 | 0.005224637066008658 |
| 10 | 11.892793170931885 | 24.82319757674545 | 960/960 | 99/99 | 0.006612978663204446 |
| 11 | 11.798072764955224 | 24.87956499937294 | 960/960 | 99/99 | 0.008164926556722473 |
| 12 | 11.696406521582613 | 24.898729311220833 | 960/960 | 99/99 | 0.009880571473789804 |
| 13 | 11.588800117161544 | 24.880697207103843 | 960/960 | 99/99 | 0.01176001372569354 |
| 14 | 11.476402086794216 | 24.825473312867217 | 960/960 | 99/99 | 0.013803363217585018 |
| 15 | 11.360516274103187 | 24.733060187031917 | 960/960 | 99/99 | 0.016010739459650557 |
| 16 | 11.242614669905949 | 24.603458322440154 | 960/960 | 99/99 | 0.018382271579212333 |
| 17 | 11.124350098410947 | 24.43666614763007 | 960/960 | 99/99 | 0.02091809833346436 |
| 18 | 11.007567984365178 | 24.232680028131615 | 960/960 | 99/99 | 0.02361836812369846 |
| 19 | 10.894316163460507 | 23.99149426763092 | 960/960 | 99/99 | 0.026483239010115223 |
| 20 | 10.78685139228056 | 23.713101108945978 | 960/960 | 99/99 | 0.02951287872775467 |
| 21 | 10.687640899514339 | 23.397490734930138 | 960/960 | 99/99 | 0.032707464703391774 |

### spc:parent-0:minus:stage-3

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格未达标
- **结果代码**: OriginalSpecificationFailed [OriginalSpecificationFailed]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.010661131462994577 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.04175867545890866 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-F2 | Standard | 5 | 未记录 | -0.044811979289588084 | 0 |  |
| parent-0-spc:back | 6.450000000000001 | Air | Standard | 5 | 未记录 | -0.03289400464092007 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 51.12739202704595 mm / 12.781848006761487

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.950006421364456 | 77.4606036808226 | 960/960 | 99/99 | 0 |
| 2 | 49.94047159319714 | 77.80992779708068 | 960/960 | 99/99 | 8.001147315416527E-05 |
| 3 | 49.91186867028527 | 78.12216224229002 | 960/960 | 99/99 | 0.00032005061929081226 |
| 4 | 49.86420234963917 | 78.39733041765257 | 960/960 | 99/99 | 0.0007201316186016879 |
| 5 | 49.79748050053875 | 78.63545359376822 | 960/960 | 99/99 | 0.0012802781068160748 |
| 6 | 49.71171422637778 | 78.83655091352382 | 960/960 | 99/99 | 0.0020005231766500255 |
| 7 | 49.606917952159364 | 79.0006393948367 | 960/960 | 99/99 | 0.0028809093822288576 |
| 8 | 49.48310953856974 | 79.12773393322547 | 960/960 | 99/99 | 0.003921488742861098 |
| 9 | 49.34031042385234 | 79.2178473042533 | 960/960 | 99/99 | 0.005122322748592134 |
| 10 | 49.17854579501102 | 79.27099016574424 | 960/960 | 99/99 | 0.006483482366062131 |
| 11 | 48.99784479021314 | 79.28717105995364 | 960/960 | 99/99 | 0.00800504804662462 |
| 12 | 48.79824073462284 | 79.26639641548783 | 960/960 | 99/99 | 0.009687109733646134 |
| 13 | 48.579771412308745 | 79.20867054911996 | 960/960 | 99/99 | 0.011529766872308278 |
| 14 | 48.34247937731064 | 79.11399566741473 | 960/960 | 99/99 | 0.013533128419649165 |
| 15 | 48.08641230746248 | 78.98237186824919 | 960/960 | 99/99 | 0.01569731285564766 |
| 16 | 47.81162340512825 | 78.81379714213632 | 960/960 | 99/99 | 0.018022448195344976 |
| 17 | 47.51817184965587 | 78.60826737341054 | 960/960 | 99/99 | 0.020508672002128192 |
| 18 | 47.206123307078755 | 78.36577634128705 | 960/960 | 99/99 | 0.023156131401582 |
| 19 | 46.87555050343057 | 78.08631572070368 | 960/960 | 99/99 | 0.025964983096876133 |
| 20 | 46.526533868993575 | 77.76987508309708 | 960/960 | 99/99 | 0.02893539338442785 |
| 21 | 46.159162261889364 | 77.41897651890037 | 960/960 | 99/99 | 0.03206753817118666 |
- **focal-length**: 0.022547840540918962 / 0.02
- **f-number**: 0.022547840540918962 / 0.02

### spc:parent-0:plus:stage-0

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.022806858048807294 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.015870339412355345 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | 0.019505365489213847 | 0 |  |
| parent-0-spc:back | 6.150000000000001 | Air | Standard | 5 | 未记录 | 0.019505383717758642 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.46223554457982 mm / 12.615558886144955

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.678377323336402 | 16.68892279665415 | 960/960 | 99/99 | 0 |
| 2 | 9.674921087629123 | 16.89206648797359 | 960/960 | 99/99 | 6.807280955992105E-05 |
| 3 | 9.664636257614706 | 17.051489992422454 | 960/960 | 99/99 | 0.00027229577910398855 |
| 4 | 9.64777538420452 | 17.167209448911148 | 960/960 | 99/99 | 0.000612682532006626 |
| 5 | 9.624762417315502 | 17.239237911815696 | 960/960 | 99/99 | 0.0010892557760531674 |
| 6 | 9.596197131586424 | 17.267585353742064 | 960/960 | 99/99 | 0.00170204730573711 |
| 7 | 9.562861062804306 | 17.252258667939667 | 960/960 | 99/99 | 0.002451098006461284 |
| 8 | 9.525724658780934 | 17.1932616703927 | 960/960 | 99/99 | 0.00333645785988026 |
| 9 | 9.485955212325685 | 17.090595101523 | 960/960 | 99/99 | 0.0043581859496012515 |
| 10 | 9.444924972050085 | 16.94425662759724 | 960/960 | 99/99 | 0.005516350468781437 |
| 11 | 9.404218616374417 | 17.267399177620433 | 960/960 | 99/99 | 0.006811028728516832 |
| 12 | 9.365639031022004 | 17.83842591248508 | 960/960 | 99/99 | 0.008242307167602936 |
| 13 | 9.331210064533732 | 18.454111610853417 | 960/960 | 99/99 | 0.009810281362824046 |
| 14 | 9.30317467882068 | 19.114506295726752 | 960/960 | 99/99 | 0.011515056041277541 |
| 15 | 9.283986710588506 | 19.819663169696298 | 960/960 | 99/99 | 0.013356745093138353 |
| 16 | 9.276294383698096 | 20.569638622248476 | 960/960 | 99/99 | 0.015335471585756178 |
| 17 | 9.282913848356515 | 21.36449223741499 | 960/960 | 99/99 | 0.01745136777899134 |
| 18 | 9.306791460588117 | 22.204286801826623 | 960/960 | 99/99 | 0.019704575141598235 |
| 19 | 9.350954323283142 | 23.08908831307921 | 960/960 | 99/99 | 0.022095244368857958 |
| 20 | 9.418449799249423 | 24.018965988513497 | 960/960 | 99/99 | 0.024623535401290243 |
| 21 | 9.512276192200355 | 24.99399227432919 | 960/960 | 99/99 | 0.027289617444264773 |

### spc:parent-0:plus:stage-1

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.022806858048807294 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.015870339412355345 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | 0.019505365489213847 | 0 |  |
| parent-0-spc:back | 6.250000000000001 | Air | Standard | 5 | 未记录 | 0.019505383717758642 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.41213787597489 mm / 12.603034468993723

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.315937606307575 | 15.887615328674338 | 960/960 | 99/99 | 0 |
| 2 | 9.313963991406833 | 16.014243152849243 | 960/960 | 99/99 | 6.774001144455838E-05 |
| 3 | 9.308135770432202 | 16.18526564749856 | 960/960 | 99/99 | 0.00027096455629900697 |
| 4 | 9.298731420815642 | 16.40070143980993 | 960/960 | 99/99 | 0.0006096871667763542 |
| 5 | 9.286217034315827 | 16.660572293724087 | 960/960 | 99/99 | 0.001083930398895707 |
| 6 | 9.271249054153037 | 16.964903113742658 | 960/960 | 99/99 | 0.00169372583444916 |
| 7 | 9.254677681044384 | 17.313721948992942 | 960/960 | 99/99 | 0.0024391140854700846 |
| 8 | 9.237550495772464 | 17.707059997751795 | 960/960 | 99/99 | 0.0033201447994351207 |
| 9 | 9.22111568403738 | 18.144951612246054 | 960/960 | 99/99 | 0.004336876664902916 |
| 10 | 9.206824072854426 | 18.62743430386793 | 960/960 | 99/99 | 0.005489377419141804 |
| 11 | 9.19632900604292 | 19.154548748676614 | 960/960 | 99/99 | 0.006777723856635764 |
| 12 | 9.191482918012719 | 19.726338793377487 | 960/960 | 99/99 | 0.008202001838383096 |
| 13 | 9.19432933993681 | 20.342851461536917 | 960/960 | 99/99 | 0.009762306302527883 |
| 14 | 9.20708903193434 | 21.00413696023029 | 960/960 | 99/99 | 0.011458741276549517 |
| 15 | 9.232139029462726 | 21.710248687039968 | 960/960 | 99/99 | 0.013291419889898258 |
| 16 | 9.271983673487963 | 22.461243237436477 | 960/960 | 99/99 | 0.015260464388003325 |
| 17 | 9.329217201688717 | 23.257180412450396 | 960/960 | 99/99 | 0.01736600614778744 |
| 18 | 9.406478220644692 | 24.098123226837576 | 960/960 | 99/99 | 0.019608185693683507 |
| 19 | 9.506397314457626 | 24.984137917477334 | 960/960 | 99/99 | 0.021987152715320233 |
| 20 | 9.631540067258548 | 25.915293952266882 | 960/960 | 99/99 | 0.024503066086216694 |
| 21 | 9.784348718138276 | 26.891664039240926 | 960/960 | 99/99 | 0.027156093883307125 |

### spc:parent-0:plus:stage-2

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.022806858048807294 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.015870339412355345 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-BK7 | Standard | 5 | 未记录 | 0.019505365489213847 | 0 |  |
| parent-0-spc:back | 6.450000000000001 | Air | Standard | 5 | 未记录 | 0.019505383717758642 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.31224036183514 mm / 12.578060090458784

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.135439394420306 | 19.62953499797345 | 960/960 | 99/99 | 0 |
| 2 | 9.136715548549544 | 19.756428821160828 | 960/960 | 99/99 | 6.707698539028241E-05 |
| 3 | 9.140640522011097 | 19.92784176772816 | 960/960 | 99/99 | 0.0002683123910896503 |
| 4 | 9.14750345995146 | 20.143792620246458 | 960/960 | 99/99 | 0.0006037195653330941 |
| 5 | 9.157784909664148 | 20.40430331912846 | 960/960 | 99/99 | 0.0010733207580038142 |
| 6 | 9.172154544483364 | 20.709398966483718 | 960/960 | 99/99 | 0.0016771471221041262 |
| 7 | 9.191467519462691 | 21.05910783025633 | 960/960 | 99/99 | 0.002415238718371039 |
| 8 | 9.21675902790421 | 21.453461348792892 | 960/960 | 99/99 | 0.0032876445202358684 |
| 9 | 9.249236548139383 | 21.89249413574501 | 960/960 | 99/99 | 0.004294422419718296 |
| 10 | 9.290269230677138 | 22.376243985336775 | 960/960 | 99/99 | 0.0054356392347533426 |
| 11 | 9.341373892131992 | 22.90475187796792 | 960/960 | 99/99 | 0.006711370717626842 |
| 12 | 9.40419716947451 | 23.47806198628133 | 960/960 | 99/99 | 0.008121701564387067 |
| 13 | 9.480493558214995 | 24.09622168147574 | 960/960 | 99/99 | 0.009666725425207861 |
| 14 | 9.57209931546681 | 24.759281540048363 | 960/960 | 99/99 | 0.011346544916627945 |
| 15 | 9.680902545174353 | 25.467295350934727 | 960/960 | 99/99 | 0.013161271633737252 |
| 16 | 9.80881017323721 | 26.220320122958718 | 960/960 | 99/99 | 0.015111026164624505 |
| 17 | 9.957712922028337 | 27.01841609270028 | 960/960 | 99/99 | 0.017195938105241792 |
| 18 | 10.129449749735715 | 27.86164673271463 | 960/960 | 99/99 | 0.01941614607561629 |
| 19 | 10.325773467118577 | 28.750078760117148 | 960/960 | 99/99 | 0.021771797737200924 |
| 20 | 10.548319327335646 | 29.683782145591753 | 960/960 | 99/99 | 0.02426304981149945 |
| 21 | 10.79857827014702 | 30.662830122735407 | 960/960 | 99/99 | 0.026890068099411434 |

### spc:parent-0:plus:stage-3

- **来源**: SPC实体补偿
- **父方案**: parent-0
- **验收**: 原规格合格
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **片数**: 2
- **机械参考 / 光阑 / 像面位置**: 0 / 1 / 55 mm
- **光阑净半口径 (mm)**: 2

| 镜片 / 面 | z mm | 材料 | 面型 | 净半口径 mm | 机械半口径 mm | c mm⁻¹ | k | 偶次系数 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| front | 3 | N-BK7 | Standard | 5 | 未记录 | 0.022806858048807294 | 0 |  |
| back | 6 | Air | Standard | 5 | 未记录 | -0.015870339412355345 | 0 |  |
| parent-0-spc:front | 6.050000000000001 | N-F2 | Standard | 5 | 未记录 | 0.019505365489213847 | 0 |  |
| parent-0-spc:back | 6.450000000000001 | Air | Standard | 5 | 未记录 | 0.019505383717758642 | 0 |  |
- **验收协议**: 907cc3de0118af8d66d19d2529737400405f08a641788db1597d77fa497c0284
- **实际焦距 / F数**: 50.27283188452823 mm / 12.568207971132058

| 视场编号 | RMS µm | 最大半径 µm | 有效 / 尝试 | 边界 / 中心 | 畸变 % |
| --- | --- | --- | --- | --- | --- |
| 1 | 9.306443921657795 | 21.20897901054609 | 960/960 | 99/99 | 0 |
| 2 | 9.309012622668787 | 21.335584978390916 | 960/960 | 99/99 | 6.692952529121693E-05 |
| 3 | 9.316810457147945 | 21.50675352381054 | 960/960 | 99/99 | 0.00026772254441113897 |
| 4 | 9.33011187885966 | 21.72250351034377 | 960/960 | 99/99 | 0.0006023923878828389 |
| 5 | 9.3493717913708 | 21.98285696487934 | 960/960 | 99/99 | 0.0010709612752178154 |
| 6 | 9.375221595670942 | 22.2878390815322 | 960/960 | 99/99 | 0.0016734603171361798 |
| 7 | 9.40846332637076 | 22.637478225807083 | 960/960 | 99/99 | 0.0024099295203359704 |
| 8 | 9.450061584489177 | 23.031805939168535 | 960/960 | 99/99 | 0.0032804177917840093 |
| 9 | 9.501132947874128 | 23.470856943974773 | 960/960 | 99/99 | 0.00428498294533746 |
| 10 | 9.562932557043991 | 23.954669148760654 | 960/960 | 99/99 | 0.005423691708494594 |
| 11 | 9.636837643634843 | 24.483283653863317 | 960/960 | 99/99 | 0.006696619731182619 |
| 12 | 9.724327896209546 | 25.05674475750519 | 960/960 | 99/99 | 0.008103851595130395 |
| 13 | 9.826962743575818 | 25.675099962147343 | 960/960 | 99/99 | 0.009645480824248945 |
| 14 | 9.9463558700893 | 26.338399981243654 | 960/960 | 99/99 | 0.011321609896603064 |
| 15 | 10.084147541680423 | 27.046698746400423 | 960/960 | 99/99 | 0.013132350257202139 |
| 16 | 10.241975586493483 | 27.800053414875457 | 960/960 | 99/99 | 0.015077822331749422 |
| 17 | 10.421446103567277 | 28.59852437744248 | 960/960 | 99/99 | 0.017158155542199113 |
| 18 | 10.624105128612815 | 29.44217526667492 | 960/960 | 99/99 | 0.019373488322521126 |
| 19 | 10.851412534843151 | 30.331072965537054 | 960/960 | 99/99 | 0.02172396813631731 |
| 20 | 11.104719369759142 | 31.265287616432325 | 960/960 | 99/99 | 0.024209751495300098 |
| 21 | 11.385249625897305 | 32.24489263057961 | 960/960 | 99/99 | 0.026831003978992452 |

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
    "elapsed": "00:00:00.1659936"
  },
  "knownFunctionEvaluations": null
}
```


### seed-1 · Validation

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
    "geometryProbes": 583,
    "elapsed": "00:00:00.1909122"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-1
- **结果代码**: NoParaxialRecovery [NoParaxialRecovery]
- **原因**: Direct recovery failed; beginning original-relative gradual deletion.

```json
{
  "work": {
    "functionEvaluations": 69,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 28,
    "differenceEvaluations": 36,
    "jacobianBuilds": 9,
    "tracedRays": 0,
    "geometryProbes": 8860,
    "elapsed": "00:00:00.0698998"
  },
  "knownFunctionEvaluations": null
}
```


### trial-0 · Recovery

- **父方案**: seed-1
- **结果代码**: NoParaxialRecovery [NoParaxialRecovery]

```json
{
  "work": {
    "functionEvaluations": 91,
    "gradientEvaluations": 0,
    "validationEvaluations": 0,
    "matrixEvaluations": 40,
    "differenceEvaluations": 52,
    "jacobianBuilds": 13,
    "tracedRays": 0,
    "geometryProbes": 32691,
    "elapsed": "00:00:00.0727803"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0 · SPC鞍点核查

- **父方案**: —
- **结果代码**: IndexOneSaddleCertified [IndexOneSaddleCertified]
- **原因**: scope=expanded-declared-curvature-subspace; negative=1; uncertainty=1.3382232282310724E-06; eigenvalues=-0.000188706702477975,0.0005637886578292615,0.08823646642212693,2991.7089435119246

```json
{
  "work": null,
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:minus:stage-0 · Thickness:1

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-BK7; center-mm=0.10000000000000053

```json
{
  "work": {
    "functionEvaluations": 9,
    "gradientEvaluations": 8,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 51672,
    "geometryProbes": 5247,
    "elapsed": "00:00:00.8155751"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:minus:stage-1 · Thickness:2

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-BK7; center-mm=0.20000000000000018

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 1,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 45414,
    "geometryProbes": 1166,
    "elapsed": "00:00:00.3247330"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:minus:stage-2 · Thickness:4

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-BK7; center-mm=0.40000000000000036

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 1,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 45414,
    "geometryProbes": 1166,
    "elapsed": "00:00:00.3947260"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:minus:stage-3 · CatalogGlass

- **父方案**: parent-0
- **结果代码**: OriginalSpecificationFailed [OriginalSpecificationFailed]
- **原因**: glass=N-F2; center-mm=0.40000000000000036

```json
{
  "work": {
    "functionEvaluations": 7,
    "gradientEvaluations": 6,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 49884,
    "geometryProbes": 4081,
    "elapsed": "00:00:00.4738970"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:minus · SPC正负分支优化

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: sign=-1; null-stop=InteriorStationary; stationary=True; positive-curvature=True

```json
{
  "work": null,
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:plus:stage-0 · Thickness:1

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-BK7; center-mm=0.10000000000000053

```json
{
  "work": {
    "functionEvaluations": 5,
    "gradientEvaluations": 4,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 48096,
    "geometryProbes": 2915,
    "elapsed": "00:00:00.3470423"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:plus:stage-1 · Thickness:2

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-BK7; center-mm=0.20000000000000018

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 1,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 45414,
    "geometryProbes": 1166,
    "elapsed": "00:00:00.2374836"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:plus:stage-2 · Thickness:4

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-BK7; center-mm=0.40000000000000036

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 1,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 45414,
    "geometryProbes": 1166,
    "elapsed": "00:00:00.2353613"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:plus:stage-3 · CatalogGlass

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: glass=N-F2; center-mm=0.40000000000000036

```json
{
  "work": {
    "functionEvaluations": 2,
    "gradientEvaluations": 1,
    "validationEvaluations": 1,
    "matrixEvaluations": 0,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 45414,
    "geometryProbes": 1166,
    "elapsed": "00:00:00.2251538"
  },
  "knownFunctionEvaluations": null
}
```


### spc:parent-0:plus · SPC正负分支优化

- **父方案**: parent-0
- **结果代码**: QualifiedOriginalSpecification [QualifiedOriginalSpecification]
- **原因**: sign=1; null-stop=InteriorStationary; stationary=True; positive-curvature=True

```json
{
  "work": null,
  "knownFunctionEvaluations": null
}
```


### preparation:status · Finished

- **父方案**: —
- **结果代码**: Completed [Completed]
- **原因**: kernel=2d2600fb32c86efd3f256de43f9991737521e718632cbfe6556347ea8ce1d22d

```json
{
  "work": {
    "functionEvaluations": 161,
    "gradientEvaluations": 151,
    "validationEvaluations": 10,
    "matrixEvaluations": 0,
    "differenceEvaluations": 56,
    "jacobianBuilds": 0,
    "tracedRays": 503394,
    "geometryProbes": 18723,
    "elapsed": "00:00:07.6748103"
  },
  "knownFunctionEvaluations": null
}
```


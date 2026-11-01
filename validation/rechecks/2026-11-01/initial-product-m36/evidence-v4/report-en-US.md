
# Automatic Initial Structure Design Report

Original F8 fixed apertures

This report presents saved evidence without recomputation or changing acceptance verdicts.
First-order feasibility, working seeds and intermediate stages do not certify the original specification. Retrieved lenses are reused designs.
Portfolio snapshots are for review and export. Exact continuation requires the original checkpoint and computation version.

## Provenance and compatibility

- **Run record**: performance-d204168dcc564dfa8190dc90ba5b521f
- **Evidence fingerprint**: d1b966edc4c4428c0a93f7c18ea635c1f7422afa64dd84215c8691c90ea31a78
- **Algorithm identity**: Cold
- **Version / revision**: 1 / 48
- **Computation version**: db5cbdaf597dd2460c22300f257a7b28e2fc86e38aca57c3c12dad981d15756d
- **Status**: Completed

## Original acceptance specification

- **Target focal length / F-number**: 50 mm / F/8
- **Focal-length / F-number relative tolerances**: 0.02 / 0.02
- **RMS / maximum-radius limits**: 50 / 150 µm
- **Distortion / valid-ray fraction**: 0.029999999999999999 / 0.97999999999999998
- **Center and rim checks**: Retain the recorded original acceptance protocol; lost check rays cannot qualify

All original targets, manufacturing limits, sampling, permissions and budgets follow without relaxation.

```json
{
  "seeds": [
    {
      "schemaVersion": 1,
      "algorithm": "AutonomousTopologyV1",
      "name": "Descriptor Thick Delano",
      "conjugate": "infinite",
      "reference": {
        "id": "reference",
        "zMillimeters": 0
      },
      "stop": {
        "id": "stop",
        "zMillimeters": 0,
        "clearSemiDiameterMillimeters": 3.125,
        "mechanicalSemiDiameterMillimeters": null
      },
      "image": {
        "id": "image",
        "zMillimeters": 65,
        "protected": true
      },
      "elements": [
        {
          "id": "generated-0",
          "frontSurfaceId": "generated-0:front",
          "backSurfaceId": "generated-0:back",
          "frontZMillimeters": 4.665815730908137,
          "thicknessMillimeters": 1.8650041384258804,
          "material": "N-BK7",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.00023870982706638931,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.02879297752319498,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-1",
          "frontSurfaceId": "generated-1:front",
          "backSurfaceId": "generated-1:back",
          "frontZMillimeters": 8.718216566014899,
          "thicknessMillimeters": 1.3308008771717166,
          "material": "N-F2",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.018338739947622078,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.022977843213564213,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-2",
          "frontSurfaceId": "generated-2:front",
          "backSurfaceId": "generated-2:back",
          "frontZMillimeters": 12.082937704109014,
          "thicknessMillimeters": 0.7969023041352958,
          "material": "N-BK7",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": 0.0012059765357857841,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": 0.013039225782330148,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-3",
          "frontSurfaceId": "generated-3:front",
          "backSurfaceId": "generated-3:back",
          "frontZMillimeters": 18.509241727768288,
          "thicknessMillimeters": 1.8398211940537266,
          "material": "N-F2",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.017547467349528362,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.0357683860986254,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-4",
          "frontSurfaceId": "generated-4:front",
          "backSurfaceId": "generated-4:back",
          "frontZMillimeters": 28.320051665899996,
          "thicknessMillimeters": 0.9699475349030564,
          "material": "N-BK7",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.035921160775212055,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.018521273070420532,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-5",
          "frontSurfaceId": "generated-5:front",
          "backSurfaceId": "generated-5:back",
          "frontZMillimeters": 32.429970285803485,
          "thicknessMillimeters": 1.455217664454608,
          "material": "N-F2",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": 0.005277892001389233,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.014676578957237826,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-6",
          "frontSurfaceId": "generated-6:front",
          "backSurfaceId": "generated-6:back",
          "frontZMillimeters": 38.72887244749896,
          "thicknessMillimeters": 1.8982207791240668,
          "material": "N-BK7",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.06805313848479408,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.07794012834304632,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        },
        {
          "id": "generated-7",
          "frontSurfaceId": "generated-7:front",
          "backSurfaceId": "generated-7:back",
          "frontZMillimeters": 43.34328291499028,
          "thicknessMillimeters": 0.6909629586900508,
          "material": "N-F2",
          "front": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.056243685872124805,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "back": {
            "profile": {
              "kind": "standard",
              "curvaturePerMillimeter": -0.03697421557824045,
              "conic": 0,
              "maximumEvenPower": 0,
              "terms": []
            },
            "clearSemiDiameterMillimeters": 5,
            "mechanicalSemiDiameterMillimeters": 5
          },
          "protected": false,
          "transition": false
        }
      ],
      "fields": [
        {
          "label": "axis",
          "xAngleDegrees": 0,
          "yAngleDegrees": 0,
          "weight": 1
        },
        {
          "label": "edge",
          "xAngleDegrees": 0,
          "yAngleDegrees": 12.5,
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
    "fNumber": 8,
    "focalLengthRelativeTolerance": 0.02,
    "fNumberRelativeTolerance": 0.02,
    "maximumRmsRadiusMillimeters": 0.05,
    "maximumSpotRadiusMillimeters": 0.15,
    "maximumDistortionFraction": 0.03,
    "minimumValidRayFraction": 0.98,
    "minimumEdgeThicknessMillimeters": 0.1,
    "minimumEdgeAirGapMillimeters": 0.05,
    "maximumOpticalTrackMillimeters": 65,
    "maximumAbsoluteSurfaceSlope": 10
  },
  "constraints": {
    "minimumElementCount": 2,
    "maximumElementCount": 8,
    "minimumCenterThicknessMillimeters": 0.1,
    "minimumAirGapMillimeters": 0.05,
    "minimumBackFocusMillimeters": 15,
    "maximumMechanicalTrackMillimeters": 65,
    "allowTransitionElements": false,
    "allowedGlassNames": []
  },
  "options": {
    "seedPreparation": {
      "coldStart": {
        "version": 6,
        "exploreBackFocus": true,
        "backFocusSlackFractions": [
          0.75,
          0.875,
          0.9375
        ],
        "attemptsPerFraction": 3,
        "maximumGradientEvaluations": 600
      },
      "generation": {
        "feasibleLayouts": {
          "version": 1,
          "maximumLayouts": 3,
          "stopAfterElementCandidates": [
            0,
            4,
            8
          ]
        },
        "materializationVersion": 3,
        "version": 1,
        "method": "delano",
        "elements": [
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-BK7"
            ]
          },
          {
            "kind": "S",
            "profile": "standard",
            "materials": [
              "N-F2"
            ]
          }
        ],
        "stopAfterElement": 0,
        "clearSemiDiameterMillimeters": 5,
        "minimumThicknessMillimeters": 0.5,
        "maximumThicknessMillimeters": 2,
        "minimumBackFocusMillimeters": 15,
        "annealingProposals": 12
      },
      "version": 1,
      "maximumStarts": 4,
      "retainedSeeds": 2,
      "maximumFunctionEvaluations": 600,
      "maximumEvaluationsPerStart": 600,
      "timeLimit": "00:10:00",
      "maximumTracedRays": 10000000,
      "focusATolerance": 0.001
    },
    "checkStageFeasibility": true,
    "protectCompensationRayBudget": true,
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
    "axialCompensation": {
      "maximumDisplacementMillimeters": 0.1,
      "coordinateScaleMillimeters": 0.1,
      "elementIds": []
    },
    "maximumTrials": 12,
    "candidatesPerElementCount": 4,
    "branchesPerParent": 6,
    "maximumFunctionEvaluations": 1000,
    "maximumGradientEvaluations": 200,
    "maximumTracedRays": 2000000,
    "maximumStageTracedRays": 400000,
    "finalValidationRayReserve": 200000,
    "timeLimit": "00:05:00",
    "seed": 45,
    "curvatureBoundPerMillimeter": 0.08,
    "insertionMaterials": [
      "N-BK7"
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
      "transmissionFirst": true,
      "activeParameterLimit": 2,
      "transmissionPolicyVersion": 4,
      "requireDescent": true,
      "maximumUpdates": 8,
      "maximumFunctionEvaluations": 9,
      "maximumGradientEvaluations": 9,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "includeBoundaryClearance": true,
        "includeBoundaryMaximum": true,
        "fieldsPerDirection": 3,
        "pupilRings": 4,
        "pupilArms": 16,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    },
    "full": {
      "transmissionFirst": true,
      "transmissionPolicyVersion": 4,
      "requireDescent": true,
      "maximumUpdates": 12,
      "maximumFunctionEvaluations": 13,
      "maximumGradientEvaluations": 13,
      "timeLimit": "00:05:00",
      "learningRate": 0.01,
      "computeMode": "cpu",
      "sampling": {
        "includeBoundaryClearance": true,
        "includeBoundaryMaximum": true,
        "fieldsPerDirection": 3,
        "pupilRings": 4,
        "pupilArms": 16,
        "profileRadialSamples": 64
      },
      "performFinalAssessment": false
    }
  }
}
```


## Budgets and actual backend

Preparation and main search are separate. Stage costs are included in cumulative costs and must not be added twice. Unrecorded is not zero work.

### Cumulative preparation cost


```json
{
  "consumed": {
    "functionEvaluations": 189,
    "gradientEvaluations": 128,
    "validationEvaluations": 6,
    "matrixEvaluations": 132,
    "differenceEvaluations": 0,
    "jacobianBuilds": 0,
    "tracedRays": 430800,
    "geometryProbes": 379318,
    "elapsed": "00:00:55.8939597"
  },
  "options": {
    "coldStart": {
      "version": 6,
      "exploreBackFocus": true,
      "backFocusSlackFractions": [
        0.75,
        0.875,
        0.9375
      ],
      "attemptsPerFraction": 3,
      "maximumGradientEvaluations": 600
    },
    "generation": {
      "feasibleLayouts": {
        "version": 1,
        "maximumLayouts": 3,
        "stopAfterElementCandidates": [
          0,
          4,
          8
        ]
      },
      "materializationVersion": 3,
      "version": 1,
      "method": "delano",
      "elements": [
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-BK7"
          ]
        },
        {
          "kind": "S",
          "profile": "standard",
          "materials": [
            "N-F2"
          ]
        }
      ],
      "stopAfterElement": 0,
      "clearSemiDiameterMillimeters": 5,
      "minimumThicknessMillimeters": 0.5,
      "maximumThicknessMillimeters": 2,
      "minimumBackFocusMillimeters": 15,
      "annealingProposals": 12
    },
    "version": 1,
    "maximumStarts": 4,
    "retainedSeeds": 2,
    "maximumFunctionEvaluations": 600,
    "maximumEvaluationsPerStart": 600,
    "timeLimit": "00:10:00",
    "maximumTracedRays": 10000000,
    "focusATolerance": 0.001
  },
  "pendingPhase": null,
  "reservation": null,
  "inFlight": null,
  "persistenceElapsed": "00:00:01.6728524",
  "lostReservations": 0
}
```

Actual execution is reported only from recorded evidence, not the selected device preference.
Not Recorded

## Candidates and original acceptance


### construction:1

- **Source**: Seed construction
- **Parent**: —
- **Acceptance**: Original specification not met
- **Outcome code**: OriginalAssessed [OriginalAssessed]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.97776444459173206 | N-BK7 | Standard | 5 | 5 | -0.061238921575338341 | 0 |  |
| generated-0:back | 2.9507173930881518 | Air | Standard | 5 | 5 | -0.055347097617733286 | 0 |  |
| generated-1:front | 3.5721866660219943 | N-F2 | Standard | 5 | 5 | 7.3535080751078357E-05 | 0 |  |
| generated-1:back | 4.8689757562899514 | Air | Standard | 5 | 5 | -0.035031396809896719 | 0 |  |
| generated-2:front | 5.3055523325811782 | N-BK7 | Standard | 5 | 5 | -0.036306649332844013 | 0 |  |
| generated-2:back | 7.083755736618853 | Air | Standard | 5 | 5 | 0.028380414560732519 | 0 |  |
| generated-3:front | 7.8620359731175835 | N-F2 | Standard | 5 | 5 | 0.031093764779765311 | 0 |  |
| generated-3:back | 8.3902449937394206 | Air | Standard | 5 | 5 | 0.029275356158563571 | 0 |  |
| generated-4:front | 8.8015794265365752 | N-BK7 | Standard | 5 | 5 | 0.050287994212631464 | 0 |  |
| generated-4:back | 9.8684726452568547 | Air | Standard | 5 | 5 | 0.013345570464494897 | 0 |  |
| generated-5:front | 10.334085099857663 | N-F2 | Standard | 5 | 5 | -0.0060278059072740803 | 0 |  |
| generated-5:back | 11.911046773434366 | Air | Standard | 5 | 5 | -0.035906807899559058 | 0 |  |
| generated-6:front | 12.778946777523437 | N-BK7 | Standard | 5 | 5 | -0.025131814407504435 | 0 |  |
| generated-6:back | 13.796278734572851 | Air | Standard | 5 | 5 | 0.0093361633588794728 | 0 |  |
| generated-7:front | 14.880321000468991 | N-F2 | Standard | 5 | 5 | -0.034532814385943647 | 0 |  |
| generated-7:back | 15.414851185579654 | Air | Standard | 5 | 5 | -0.052712663612884764 | 0 |  |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 49.999999999999979 mm / 7.9999999999999964

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 107.77432360871865 | 280.30617769968342 | 960/960 | 99/99 | 0 |
| 2 | 108.05030798726898 | 290.09495633010351 | 960/960 | 99/99 | 0.003059360963480908 |
| 3 | 108.87935620725924 | 300.98372092758274 | 960/960 | 99/99 | 0.012241172331005998 |
| 4 | 110.26474598884387 | 313.00174475770535 | 960/960 | 99/99 | 0.02755663307867438 |
| 5 | 112.21191680443762 | 326.18242117867612 | 960/960 | 99/99 | 0.049024453404504641 |
| 6 | 114.72844047811699 | 340.56345630621098 | 960/960 | 99/99 | 0.076670922904856526 |
| 7 | 117.82399036949768 | 356.1870927051225 | 960/960 | 99/99 | 0.11053000665219966 |
| 8 | 121.51031734162382 | 373.10036641452183 | 960/960 | 99/99 | 0.15064346982444682 |
| 9 | 125.80124060451057 | 391.35540001120586 | 960/960 | 99/99 | 0.19706103172019204 |
| 10 | 130.71266043229991 | 411.00973485854644 | 960/960 | 99/99 | 0.24984055020431414 |
| 11 | 136.26259805540246 | 432.12670618632563 | 960/960 | 99/99 | 0.30904823783501384 |
| 12 | 142.47126615448886 | 454.77586521085198 | 960/960 | 99/99 | 0.37475891116403115 |
| 13 | 149.3611716944057 | 479.03345314498137 | 960/960 | 99/99 | 0.44705627492805083 |
| 14 | 156.95725157347258 | 504.98293268004522 | 960/960 | 99/99 | 0.52603324312540101 |
| 15 | 165.28704083124109 | 532.71558336500743 | 960/960 | 99/99 | 0.61179229924316902 |
| 16 | 174.38087294558119 | 562.33116827850085 | 960/960 | 99/99 | 0.70444589821965564 |
| 17 | 184.2721119756327 | 593.93868051862592 | 960/960 | 99/99 | 0.8041169130477106 |
| 18 | 193.38050594188195 | 601.88872781403404 | 951/960 | 86/99 | 0.91093912931143028 |
| 19 | 198.12187857494303 | 605.67090397410925 | 930/960 | 80/99 | 1.0250577913405137 |
| 20 | 203.43231179009038 | 601.63677103769692 | 911/960 | 78/99 | 1.1466302041226413 |
| 21 | 210.17636822725015 | 586.73378992007713 | 897/960 | 72/99 | 1.2758263956204452 |
- **field:0:rms**: 0.10777432360871865 / 0.050000000000000003
- **field:0:maximum**: 0.28030617769968341 / 0.14999999999999999
- **field:1:rms**: 0.10805030798726897 / 0.050000000000000003
- **field:1:maximum**: 0.29009495633010352 / 0.14999999999999999
- **field:2:rms**: 0.10887935620725923 / 0.050000000000000003
- **field:2:maximum**: 0.30098372092758274 / 0.14999999999999999
- **field:3:rms**: 0.11026474598884387 / 0.050000000000000003
- **field:3:maximum**: 0.31300174475770537 / 0.14999999999999999
- **field:4:rms**: 0.11221191680443762 / 0.050000000000000003
- **field:4:maximum**: 0.32618242117867613 / 0.14999999999999999
- **field:5:rms**: 0.11472844047811699 / 0.050000000000000003
- **field:5:maximum**: 0.34056345630621099 / 0.14999999999999999
- **field:6:rms**: 0.11782399036949769 / 0.050000000000000003
- **field:6:maximum**: 0.3561870927051225 / 0.14999999999999999
- **field:7:rms**: 0.12151031734162382 / 0.050000000000000003
- **field:7:maximum**: 0.37310036641452182 / 0.14999999999999999
- **field:8:rms**: 0.12580124060451056 / 0.050000000000000003
- **field:8:maximum**: 0.39135540001120583 / 0.14999999999999999
- **field:9:rms**: 0.13071266043229993 / 0.050000000000000003
- **field:9:maximum**: 0.41100973485854642 / 0.14999999999999999
- **field:10:rms**: 0.13626259805540247 / 0.050000000000000003
- **field:10:maximum**: 0.43212670618632565 / 0.14999999999999999
- **field:11:rms**: 0.14247126615448885 / 0.050000000000000003
- **field:11:maximum**: 0.454775865210852 / 0.14999999999999999
- **field:12:rms**: 0.14936117169440569 / 0.050000000000000003
- **field:12:maximum**: 0.47903345314498136 / 0.14999999999999999
- **field:13:rms**: 0.15695725157347257 / 0.050000000000000003
- **field:13:maximum**: 0.50498293268004524 / 0.14999999999999999
- **field:14:rms**: 0.1652870408312411 / 0.050000000000000003
- **field:14:maximum**: 0.53271558336500746 / 0.14999999999999999
- **field:15:rms**: 0.17438087294558119 / 0.050000000000000003
- **field:15:maximum**: 0.56233116827850083 / 0.14999999999999999
- **field:16:rms**: 0.1842721119756327 / 0.050000000000000003
- **field:16:maximum**: 0.59393868051862597 / 0.14999999999999999
- **field:17:check-lost**: 13 / 0
- **field:17:rms**: 0.19338050594188194 / 0.050000000000000003
- **field:17:maximum**: 0.60188872781403402 / 0.14999999999999999
- **field:18:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:0:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:1:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:2:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:check-lost**: 19 / 0
- **field:18:rms**: 0.19812187857494304 / 0.050000000000000003
- **field:18:maximum**: 0.60567090397410928 / 0.14999999999999999
- **field:19:lost-fraction**: 0.051041666666666652 / 0.020000000000000018
- **field:19:wave:0:lost-fraction**: 0.046875 / 0.020000000000000018
- **field:19:wave:1:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:19:wave:2:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:19:check-lost**: 21 / 0
- **field:19:rms**: 0.20343231179009039 / 0.050000000000000003
- **field:19:maximum**: 0.60163677103769686 / 0.14999999999999999
- **field:20:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:wave:0:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:wave:1:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:wave:2:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:check-lost**: 27 / 0
- **field:20:rms**: 0.21017636822725014 / 0.050000000000000003
- **field:20:maximum**: 0.58673378992007708 / 0.14999999999999999

### construction:2

- **Source**: Seed construction
- **Parent**: —
- **Acceptance**: Original specification not met
- **Outcome code**: OriginalAssessed [OriginalAssessed]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.5223047073100896 | N-BK7 | Standard | 5 | 5 | -0.016298618371112683 | 0 |  |
| generated-0:back | 1.8318563273357462 | Air | Standard | 5 | 5 | -0.014472438463227844 | 0 |  |
| generated-1:front | 2.165853980575287 | N-F2 | Standard | 5 | 5 | -0.0047472965613979913 | 0 |  |
| generated-1:back | 3.2583007195512668 | Air | Standard | 5 | 5 | -0.0078956468745459384 | 0 |  |
| generated-2:front | 3.6176512974363924 | N-BK7 | Standard | 5 | 5 | 0.00096568738962034436 | 0 |  |
| generated-2:back | 4.242986178998442 | Air | Standard | 5 | 5 | -0.0042091324586185398 | 0 |  |
| generated-3:front | 4.5680988146449382 | N-F2 | Standard | 5 | 5 | -0.012097451400890361 | 0 |  |
| generated-3:back | 6.2626757324370246 | Air | Standard | 5 | 5 | -0.0083282547824011597 | 0 |  |
| generated-4:front | 6.9705440367110061 | N-BK7 | Standard | 5 | 5 | -0.0069764904206913222 | 0 |  |
| generated-4:back | 8.6457040550933364 | Air | Standard | 5 | 5 | -0.019552912596872231 | 0 |  |
| generated-5:front | 8.9966978045836132 | N-F2 | Standard | 5 | 5 | -0.01287965215770087 | 0 |  |
| generated-5:back | 10.801489091129824 | Air | Standard | 5 | 5 | -0.012010632781688941 | 0 |  |
| generated-6:front | 11.386895535835288 | N-BK7 | Standard | 5 | 5 | -0.010834728051823179 | 0 |  |
| generated-6:back | 13.126168369745427 | Air | Standard | 5 | 5 | -0.014110373258057637 | 0 |  |
| generated-7:front | 13.761133746430513 | N-F2 | Standard | 5 | 5 | -0.005404868197060827 | 0 |  |
| generated-7:back | 15.392008075392837 | Air | Standard | 5 | 5 | -0.023006445892353046 | 0 |  |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 49.999999999999979 mm / 7.9999999999999964

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 28.980652537810538 | 77.270274339758956 | 960/960 | 99/99 | 0 |
| 2 | 29.129488441617138 | 78.872635936936945 | 960/960 | 99/99 | 0.0030445445230872135 |
| 3 | 29.576490416851147 | 81.010688225157779 | 960/960 | 99/99 | 0.012179850626786644 |
| 4 | 30.323069943948116 | 83.685057760894296 | 960/960 | 99/99 | 0.027410936443648361 |
| 5 | 31.371360264487468 | 86.896419274977717 | 960/960 | 99/99 | 0.04874616729045117 |
| 6 | 32.723944980191838 | 90.645492970101486 | 960/960 | 99/99 | 0.076197258298716763 |
| 7 | 34.383570798181559 | 94.933041161413101 | 960/960 | 99/99 | 0.10977927807091718 |
| 8 | 36.352896949720545 | 99.759864246459884 | 960/960 | 99/99 | 0.14951065334043093 |
| 9 | 38.634313474714247 | 105.1267959896789 | 960/960 | 99/99 | 0.19541317459585003 |
| 10 | 41.229837446471173 | 111.03469810423228 | 960/960 | 99/99 | 0.24751200263435583 |
| 11 | 44.141078248279136 | 117.48445411275377 | 960/960 | 99/99 | 0.30583567599301192 |
| 12 | 47.369253511466056 | 124.47696246649275 | 960/960 | 99/99 | 0.37041611920161455 |
| 13 | 50.915235496027499 | 132.01312890096872 | 960/960 | 99/99 | 0.44128865179137167 |
| 14 | 54.7796107308233 | 140.09385800429897 | 960/960 | 99/99 | 0.51849199798809298 |
| 15 | 58.962740719158298 | 148.7200439728804 | 960/960 | 99/99 | 0.60206829700717734 |
| 16 | 63.464816434623067 | 157.89256052778811 | 960/960 | 99/99 | 0.69206311385677521 |
| 17 | 68.285903197296378 | 167.61224996331947 | 960/960 | 99/99 | 0.78852545055270173 |
| 18 | 73.461573032900674 | 177.60096850800977 | 947/960 | 84/99 | 0.89150775763291601 |
| 19 | 78.727700189125102 | 187.45968182628127 | 930/960 | 78/99 | 1.0010659458505309 |
| 20 | 84.332856837043551 | 197.34873106719419 | 909/960 | 78/99 | 1.117259397916593 |
| 21 | 90.303415396747681 | 208.24214850001255 | 897/960 | 72/99 | 1.2401509801509827 |
- **field:12:rms**: 0.0509152354960275 / 0.050000000000000003
- **field:13:rms**: 0.054779610730823297 / 0.050000000000000003
- **field:14:rms**: 0.058962740719158295 / 0.050000000000000003
- **field:15:rms**: 0.063464816434623067 / 0.050000000000000003
- **field:15:maximum**: 0.15789256052778811 / 0.14999999999999999
- **field:16:rms**: 0.068285903197296383 / 0.050000000000000003
- **field:16:maximum**: 0.16761224996331947 / 0.14999999999999999
- **field:17:check-lost**: 15 / 0
- **field:17:rms**: 0.073461573032900671 / 0.050000000000000003
- **field:17:maximum**: 0.17760096850800977 / 0.14999999999999999
- **field:18:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:0:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:1:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:wave:2:lost-fraction**: 0.03125 / 0.020000000000000018
- **field:18:check-lost**: 21 / 0
- **field:18:rms**: 0.078727700189125102 / 0.050000000000000003
- **field:18:maximum**: 0.18745968182628125 / 0.14999999999999999
- **field:19:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:19:wave:0:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:19:wave:1:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:19:wave:2:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:19:check-lost**: 21 / 0
- **field:19:rms**: 0.084332856837043557 / 0.050000000000000003
- **field:19:maximum**: 0.19734873106719419 / 0.14999999999999999
- **field:20:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:wave:0:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:wave:1:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:wave:2:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:20:check-lost**: 27 / 0
- **field:20:rms**: 0.090303415396747688 / 0.050000000000000003
- **field:20:maximum**: 0.20824214850001255 / 0.14999999999999999

### construction:3

- **Source**: Seed construction
- **Parent**: —
- **Acceptance**: Original specification not met
- **Outcome code**: OriginalAssessed [OriginalAssessed]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.25820890296696059 | N-BK7 | Standard | 5 | 5 | 0.013370693940111707 | 0 |  |
| generated-0:back | 1.3698063981214279 | Air | Standard | 5 | 5 | -0.0012581071317331095 | 0 |  |
| generated-1:front | 1.9036188509220082 | N-F2 | Standard | 5 | 5 | -0.022135700969556832 | 0 |  |
| generated-1:back | 3.5228132012070779 | Air | Standard | 5 | 5 | 0.027959110477209781 | 0 |  |
| generated-2:front | 3.9109527744128649 | N-BK7 | Standard | 5 | 5 | 0.041046853820231731 | 0 |  |
| generated-2:back | 5.0926593275810825 | Air | Standard | 5 | 5 | 0.02030095564198019 | 0 |  |
| generated-3:front | 5.8516581379934447 | N-F2 | Standard | 5 | 5 | 0.014884494946703205 | 0 |  |
| generated-3:back | 7.5361721221773976 | Air | Standard | 5 | 5 | 0.009941576274589246 | 0 |  |
| generated-4:front | 8.4432705434328827 | N-BK7 | Standard | 5 | 5 | 0.047534842368859541 | 0 |  |
| generated-4:back | 9.1760803185399702 | Air | Standard | 5 | 5 | 0.020270867083341214 | 0 |  |
| generated-5:front | 10.112662382658606 | N-F2 | Standard | 5 | 5 | -0.0083717596424441546 | 0 |  |
| generated-5:back | 11.735707684563131 | Air | Standard | 5 | 5 | -0.028297877119511126 | 0 |  |
| generated-6:front | 12.476260763176734 | N-BK7 | Standard | 5 | 5 | -0.049276143849274458 | 0 |  |
| generated-6:back | 13.997727697989527 | Air | Standard | 5 | 5 | -0.03306289707187595 | 0 |  |
| generated-7:front | 14.329501646477278 | N-F2 | Standard | 5 | 5 | 0.019664161384139081 | 0 |  |
| generated-7:back | 15.093346375505586 | Air | Standard | 5 | 5 | 0.0027478085171560741 | 0 |  |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 50.000000000000043 mm / 8.0000000000000071

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 26.93127377568797 | 70.786953959396641 | 960/960 | 99/99 | 0 |
| 2 | 27.566456377950686 | 81.627956281977163 | 960/960 | 99/99 | 0.0049841812174659115 |
| 3 | 29.449150531194007 | 94.493001599915914 | 960/960 | 99/99 | 0.019942011321892505 |
| 4 | 32.524357252819676 | 109.40064310311514 | 960/960 | 99/99 | 0.044889356713385939 |
| 5 | 36.732300222220353 | 126.37294131501298 | 960/960 | 99/99 | 0.079852684864542361 |
| 6 | 42.028281699653313 | 145.435522852726 | 960/960 | 99/99 | 0.12486909951563338 |
| 7 | 48.388416980979365 | 166.61764913466382 | 960/960 | 99/99 | 0.17998638992115032 |
| 8 | 55.80662271394295 | 189.95229539808145 | 960/960 | 99/99 | 0.24526309411656891 |
| 9 | 64.289205681589578 | 215.47624044776015 | 960/960 | 99/99 | 0.3207685761695353 |
| 10 | 73.85021955111408 | 243.23016762258564 | 960/960 | 99/99 | 0.40658311736807057 |
| 11 | 84.508349421649186 | 273.25877753920838 | 960/960 | 99/99 | 0.502798021286054 |
| 12 | 96.285100476000466 | 305.61091325382517 | 960/960 | 99/99 | 0.60951573265730485 |
| 13 | 109.20386663833357 | 340.33969857489853 | 960/960 | 99/99 | 0.72684996997392048 |
| 14 | 123.28952670874817 | 377.50269036407946 | 960/960 | 99/99 | 0.85492587171553824 |
| 15 | 138.56833287946392 | 417.16204578085313 | 960/960 | 99/99 | 0.99388015609447167 |
| 16 | 155.06795084933324 | 459.38470556306487 | 960/960 | 99/99 | 1.1438612941949371 |
| 17 | 170.31469893749141 | 469.60056080683324 | 948/960 | 84/99 | 1.30502969636182 |
| 18 | 183.07164216561188 | 484.78990834023074 | 924/960 | 78/99 | 1.4775579116813913 |
| 19 | 195.22096357031566 | 503.66226543687833 | 907/960 | 78/99 | 1.6616308403806328 |
| 20 | 211.24173861861627 | 522.70539154164555 | 895/960 | 72/99 | 1.8574459589561731 |
| 21 | 218.59619956184275 | 503.81526078778393 | 870/960 | 72/99 | 2.0652135578315938 |
- **field:6:maximum**: 0.16661764913466381 / 0.14999999999999999
- **field:7:rms**: 0.055806622713942947 / 0.050000000000000003
- **field:7:maximum**: 0.18995229539808145 / 0.14999999999999999
- **field:8:rms**: 0.064289205681589578 / 0.050000000000000003
- **field:8:maximum**: 0.21547624044776015 / 0.14999999999999999
- **field:9:rms**: 0.07385021955111408 / 0.050000000000000003
- **field:9:maximum**: 0.24323016762258565 / 0.14999999999999999
- **field:10:rms**: 0.084508349421649184 / 0.050000000000000003
- **field:10:maximum**: 0.27325877753920835 / 0.14999999999999999
- **field:11:rms**: 0.09628510047600046 / 0.050000000000000003
- **field:11:maximum**: 0.30561091325382517 / 0.14999999999999999
- **field:12:rms**: 0.10920386663833356 / 0.050000000000000003
- **field:12:maximum**: 0.34033969857489854 / 0.14999999999999999
- **field:13:rms**: 0.12328952670874817 / 0.050000000000000003
- **field:13:maximum**: 0.37750269036407946 / 0.14999999999999999
- **field:14:rms**: 0.13856833287946393 / 0.050000000000000003
- **field:14:maximum**: 0.41716204578085314 / 0.14999999999999999
- **field:15:rms**: 0.15506795084933325 / 0.050000000000000003
- **field:15:maximum**: 0.45938470556306488 / 0.14999999999999999
- **field:16:check-lost**: 15 / 0
- **field:16:rms**: 0.1703146989374914 / 0.050000000000000003
- **field:16:maximum**: 0.46960056080683327 / 0.14999999999999999
- **field:17:lost-fraction**: 0.037499999999999978 / 0.020000000000000018
- **field:17:wave:0:lost-fraction**: 0.037499999999999978 / 0.020000000000000018
- **field:17:wave:1:lost-fraction**: 0.037499999999999978 / 0.020000000000000018
- **field:17:wave:2:lost-fraction**: 0.037499999999999978 / 0.020000000000000018
- **field:17:check-lost**: 21 / 0
- **field:17:rms**: 0.18307164216561186 / 0.050000000000000003
- **field:17:maximum**: 0.48478990834023072 / 0.14999999999999999
- **field:18:lost-fraction**: 0.055208333333333304 / 0.020000000000000018
- **field:18:wave:0:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:18:wave:1:lost-fraction**: 0.053124999999999978 / 0.020000000000000018
- **field:18:wave:2:lost-fraction**: 0.059374999999999956 / 0.020000000000000018
- **field:18:check-lost**: 21 / 0
- **field:18:rms**: 0.19522096357031565 / 0.050000000000000003
- **field:18:maximum**: 0.50366226543687831 / 0.14999999999999999
- **field:19:lost-fraction**: 0.06770833333333337 / 0.020000000000000018
- **field:19:wave:0:lost-fraction**: 0.065625000000000044 / 0.020000000000000018
- **field:19:wave:1:lost-fraction**: 0.068749999999999978 / 0.020000000000000018
- **field:19:wave:2:lost-fraction**: 0.068749999999999978 / 0.020000000000000018
- **field:19:check-lost**: 27 / 0
- **field:19:rms**: 0.21124173861861625 / 0.050000000000000003
- **field:19:maximum**: 0.52270539154164553 / 0.14999999999999999
- **field:20:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:wave:0:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:wave:1:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:wave:2:lost-fraction**: 0.09375 / 0.020000000000000018
- **field:20:check-lost**: 27 / 0
- **field:20:rms**: 0.21859619956184276 / 0.050000000000000003
- **field:20:maximum**: 0.50381526078778394 / 0.14999999999999999

### compensation:0

- **Source**: Compensation
- **Parent**: —
- **Acceptance**: Original specification not met
- **Outcome code**: Budget Exhausted [BudgetExhausted]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.42230470731008962 | N-BK7 | Standard | 5 | 5 | -0.072753976067805703 | 0 |  |
| generated-0:back | 1.9318563273357463 | Air | Standard | 5 | 5 | -0.0093535582137170923 | 0 |  |
| generated-1:front | 2.0658539805752869 | N-F2 | Standard | 5 | 5 | -0.013015228552201786 | 0 |  |
| generated-1:back | 3.3583007195512669 | Air | Standard | 5 | 5 | -0.0056817098503877325 | 0 |  |
| generated-2:front | 3.5176512974363923 | N-BK7 | Standard | 5 | 5 | -0.0069841896075678936 | 0 |  |
| generated-2:back | 4.3429861789984416 | Air | Standard | 5 | 5 | -0.041171828241516856 | 0 |  |
| generated-3:front | 4.4680988146449385 | N-F2 | Standard | 5 | 5 | -0.0087613904351291749 | 0 |  |
| generated-3:back | 6.3626757324370242 | Air | Standard | 5 | 5 | -0.034337317856481961 | 0 |  |
| generated-4:front | 6.87054403671101 | N-BK7 | Standard | 5 | 5 | -0.0012235962766148744 | 0 |  |
| generated-4:back | 8.745704055093336 | Air | Standard | 5 | 5 | -0.043828459769176854 | 0 |  |
| generated-5:front | 8.8966978045836207 | N-F2 | Standard | 5 | 5 | 0.0038895961102698357 | 0 |  |
| generated-5:back | 10.901489091129809 | Air | Standard | 5 | 5 | -0.017690373260248395 | 0 |  |
| generated-6:front | 11.286895535835317 | N-BK7 | Standard | 5 | 5 | 0.0066429821822111554 | 0 |  |
| generated-6:back | 13.226168367731766 | Air | Standard | 5 | 5 | 0.014323839979168786 | 0 |  |
| generated-7:front | 13.707922285350861 | N-F2 | Standard | 5 | 5 | -0.020146689093535709 | 0 |  |
| generated-7:back | 15.292008075392856 | Air | Standard | 5 | 5 | 0.0067207145485994862 | 0 |  |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 50.96697323771302 mm / 8.1547157180340832

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 42.224008229765097 | 120.94135823714215 | 960/960 | 99/99 | 0 |
| 2 | 42.297229334523031 | 121.11201123901438 | 960/960 | 99/99 | 0.0027530278119092589 |
| 3 | 42.517396171855474 | 121.61872188517897 | 960/960 | 99/99 | 0.011013636484781352 |
| 4 | 42.885989396004042 | 122.46350008941032 | 960/960 | 99/99 | 0.024786402648842493 |
| 5 | 43.405379485010442 | 123.64817017144824 | 960/960 | 99/99 | 0.044078957083037759 |
| 6 | 44.078689118176584 | 125.17437546166255 | 960/960 | 99/99 | 0.068901989301318745 |
| 7 | 44.909616260648029 | 127.04358216761769 | 960/960 | 99/99 | 0.099269253967648019 |
| 8 | 45.902231809115946 | 129.25708251459599 | 960/960 | 99/99 | 0.13519757912670016 |
| 9 | 47.06076704614437 | 131.81599717208138 | 960/960 | 99/99 | 0.17670687624194908 |
| 10 | 48.389405942098172 | 134.72127697689552 | 960/960 | 99/99 | 0.22382015202503266 |
| 11 | 49.892095588339586 | 137.97370396392682 | 960/960 | 99/99 | 0.27656352204049012 |
| 12 | 51.572385034485897 | 141.57389171433277 | 960/960 | 99/99 | 0.33496622606541199 |
| 13 | 53.433299020680309 | 145.52228503134134 | 960/960 | 99/99 | 0.39906064518035772 |
| 14 | 55.477249123219792 | 149.81915895310749 | 960/960 | 99/99 | 0.46888232056529661 |
| 15 | 57.705981220882073 | 154.46461711267733 | 960/960 | 99/99 | 0.54446997397025643 |
| 16 | 60.120555355479411 | 159.45858945430837 | 960/960 | 99/99 | 0.62586552982625021 |
| 17 | 62.721352220223821 | 164.80082931696495 | 960/960 | 99/99 | 0.71311413895753017 |
| 18 | 65.508099676724015 | 170.49090989497807 | 960/960 | 99/99 | 0.80626420385202258 |
| 19 | 68.479912730645111 | 176.52822008772873 | 960/960 | 99/99 | 0.90536740544439054 |
| 20 | 71.635341051112874 | 182.91195975061214 | 960/960 | 99/99 | 1.0104787313550534 |
| 21 | 74.900766068229075 | 187.22567753043666 | 953/960 | 90/99 | 1.1216565055279952 |
- **field:11:rms**: 0.051572385034485899 / 0.050000000000000003
- **field:12:rms**: 0.053433299020680308 / 0.050000000000000003
- **field:13:rms**: 0.055477249123219795 / 0.050000000000000003
- **field:14:rms**: 0.057705981220882073 / 0.050000000000000003
- **field:14:maximum**: 0.15446461711267734 / 0.14999999999999999
- **field:15:rms**: 0.060120555355479409 / 0.050000000000000003
- **field:15:maximum**: 0.15945858945430835 / 0.14999999999999999
- **field:16:rms**: 0.062721352220223819 / 0.050000000000000003
- **field:16:maximum**: 0.16480082931696494 / 0.14999999999999999
- **field:17:rms**: 0.06550809967672401 / 0.050000000000000003
- **field:17:maximum**: 0.17049090989497806 / 0.14999999999999999
- **field:18:rms**: 0.068479912730645118 / 0.050000000000000003
- **field:18:maximum**: 0.17652822008772873 / 0.14999999999999999
- **field:19:rms**: 0.071635341051112869 / 0.050000000000000003
- **field:19:maximum**: 0.18291195975061214 / 0.14999999999999999
- **field:20:check-lost**: 9 / 0
- **field:20:rms**: 0.074900766068229069 / 0.050000000000000003
- **field:20:maximum**: 0.18722567753043665 / 0.14999999999999999

### compensation:1

- **Source**: Compensation
- **Parent**: —
- **Acceptance**: Original specification not met
- **Outcome code**: Phase Converged [Converged]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.42230470731008962 | N-BK7 | EvenAsphere | 5 | 5 | -0.072687083939768027 | 0 | r4=-3.1999999999999999E-05 |
| generated-0:back | 1.9318563273357463 | Air | EvenAsphere | 5 | 5 | -0.0098835651864793064 | 0 | r4=1.1936750555783783E-05 |
| generated-1:front | 2.0658539805752869 | N-F2 | EvenAsphere | 5 | 5 | -0.012635346612801834 | 0 | r4=-9.7213714513523078E-06 |
| generated-1:back | 3.3583007195512669 | Air | EvenAsphere | 5 | 5 | -0.0060036377983692822 | 0 | r4=-4.1337035721774709E-06 |
| generated-2:front | 3.5176512974363923 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900537101649202 | 0 | r4=7.1259470614816511E-06 |
| generated-2:back | 4.3429861789984416 | Air | EvenAsphere | 5 | 5 | -0.041390669841257041 | 0 | r4=1.099233072402861E-06 |
| generated-3:front | 4.4680988146449385 | N-F2 | EvenAsphere | 5 | 5 | -0.008532518140849871 | 0 | r4=8.5302845570652123E-06 |
| generated-3:back | 6.3626757324370242 | Air | EvenAsphere | 5 | 5 | -0.034428402655285746 | 0 | r4=-6.9416514868645271E-06 |
| generated-4:front | 6.8705440367110064 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138862022038092 | 0 | r4=1.4851943837795802E-05 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.043841694671256128 | 0 | r4=-1.0212902277023412E-05 |
| generated-5:front | 8.8966978045836136 | N-F2 | EvenAsphere | 5 | 5 | 0.0037828140192434612 | 0 | r4=6.5085601457006584E-06 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469708923416278 | 0 | r4=-2.7949698673625297E-06 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.0061821119365386258 | 0 | r4=-5.1040758147783425E-06 |
| generated-6:back | 13.221238698920514 | Air | EvenAsphere | 5 | 5 | 0.014482506001003086 | 0 | r4=5.8832273177683365E-06 |
| generated-7:front | 13.713642723975546 | N-F2 | EvenAsphere | 5 | 5 | -0.020275159158561405 | 0 | r4=-5.3803897882895277E-06 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.0078367367097027553 | 0 | r4=3.1999999999999999E-05 |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 50.968399589102852 mm / 8.1549439342564565

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 50.063352171799743 | 135.44679021408729 | 960/960 | 99/99 | 0 |
| 2 | 50.055998511480155 | 136.94506367240976 | 960/960 | 99/99 | 0.0019480152937041686 |
| 3 | 50.034022038401083 | 138.24923381419097 | 960/960 | 99/99 | 0.0077924738894078228 |
| 4 | 49.997685283718397 | 139.35543444414211 | 960/960 | 99/99 | 0.017534613129808054 |
| 5 | 49.947456085480106 | 140.25854474323029 | 960/960 | 99/99 | 0.03117649257893065 |
| 6 | 49.884053941550071 | 140.95217298285201 | 960/960 | 99/99 | 0.048720989780061195 |
| 7 | 49.808516748240173 | 141.42863604282451 | 960/960 | 99/99 | 0.070171795295809858 |
| 8 | 49.722289755533154 | 141.70184277031137 | 960/960 | 99/99 | 0.09553340398309336 |
| 9 | 49.627339049413017 | 141.96666783003141 | 960/960 | 99/99 | 0.12481110723438758 |
| 10 | 49.526292282547452 | 142.25240451158524 | 960/960 | 99/99 | 0.1580109819244499 |
| 11 | 49.422609642900653 | 142.55564645388154 | 960/960 | 99/99 | 0.1951398772858457 |
| 12 | 49.320788072685332 | 142.88831510588736 | 960/960 | 99/99 | 0.23620540073483165 |
| 13 | 49.226601357921695 | 143.29901046659367 | 960/960 | 99/99 | 0.28121590157235066 |
| 14 | 49.147377674779683 | 143.65602590271692 | 960/960 | 99/99 | 0.33018045293993686 |
| 15 | 49.092314192733234 | 143.95722253369351 | 960/960 | 99/99 | 0.38310883081077407 |
| 16 | 49.072825005276094 | 144.36800101917004 | 960/960 | 99/99 | 0.44001149175264215 |
| 17 | 49.102913599702774 | 144.88481162731389 | 960/960 | 99/99 | 0.50089954802885339 |
| 18 | 49.199553958735144 | 145.38172861903354 | 960/960 | 99/99 | 0.56578474054303407 |
| 19 | 49.383055187916632 | 145.85759603585814 | 960/960 | 99/99 | 0.63467940872842332 |
| 20 | 49.677373895957224 | 146.31178306670188 | 960/960 | 99/99 | 0.70759645853614606 |
| 21 | 50.110328063025008 | 146.74434821523192 | 960/960 | 99/99 | 0.78454932678252276 |
- **field:0:rms**: 0.05006335217179974 / 0.050000000000000003
- **field:1:rms**: 0.050055998511480154 / 0.050000000000000003
- **field:2:rms**: 0.050034022038401085 / 0.050000000000000003
- **field:20:rms**: 0.050110328063025011 / 0.050000000000000003

### compensation:2

- **Source**: Compensation
- **Parent**: —
- **Acceptance**: Qualified at original specification
- **Outcome code**: Phase Converged [Converged]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.42230470731008962 | N-BK7 | EvenAsphere | 5 | 5 | -0.072687627630622406 | 0 | r4=-3.1999999999999999E-05; r6=-1.0083865252042736E-08 |
| generated-0:back | 1.931601937415051 | Air | EvenAsphere | 5 | 5 | -0.0098835397274240882 | 0 | r4=1.1981900888828898E-05; r6=2.6269722961748997E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.01263542855709463 | 0 | r4=-9.7576980535600643E-06; r6=-1.9839016887037749E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.0060035796147400369 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900688541994405 | 0 | r4=7.1075365967821743E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.3427714596272224 | Air | EvenAsphere | 5 | 5 | -0.041390550229016887 | 0 | r4=1.1133670444951489E-06; r6=-6.4770522548475613E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.008532577379467202 | 0 | r4=8.5216921196377692E-06; r6=6.4757151809067226E-10 |
| generated-3:back | 6.3626757324370242 | Air | EvenAsphere | 5 | 5 | -0.034428315886787443 | 0 | r4=-6.9412825210738727E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.8705440367110064 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138665733055055 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.043841723156710539 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.8966978045836136 | N-F2 | EvenAsphere | 5 | 5 | 0.0037827970618371808 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469733482168688 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.0061822131685786682 | 0 | r4=-5.0933524788400034E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.014482451783158911 | 0 | r4=5.8831481339419875E-06; r6=-6.8969645244499652E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.020275085670629731 | 0 | r4=-5.3828485393541194E-06; r6=-6.5464190732827016E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.0078366260963341552 | 0 | r4=3.1999999999999999E-05; r6=4.5603689353516439E-09 |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 50.968399607312655 mm / 8.1549439371700245

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.915503519408425 | 135.13725385288254 | 960/960 | 99/99 | 0 |
| 2 | 49.908249765642601 | 136.58887397255259 | 960/960 | 99/99 | 0.001948536150955615 |
| 3 | 49.886568811560601 | 137.84871132422992 | 960/960 | 99/99 | 0.0077945466280237704 |
| 4 | 49.850710576392387 | 138.91287163137943 | 960/960 | 99/99 | 0.017539236663393565 |
| 5 | 49.801121848245074 | 139.77619959391109 | 960/960 | 99/99 | 0.031184612169124003 |
| 6 | 49.738492602948462 | 140.43226238692876 | 960/960 | 99/99 | 0.048733475293744201 |
| 7 | 49.663822718435938 | 140.87332891991844 | 960/960 | 99/99 | 0.070189419179383955 |
| 8 | 49.578510942335079 | 141.13250973982525 | 960/960 | 99/99 | 0.09555681887029116 |
| 9 | 49.484468462752204 | 141.39429364868477 | 960/960 | 99/99 | 0.12484082310332478 |
| 10 | 49.384259859542674 | 141.69474638867629 | 960/960 | 99/99 | 0.15804734271797519 |
| 11 | 49.281274503240418 | 141.97682030667499 | 960/960 | 99/99 | 0.19518303690447472 |
| 12 | 49.179931519049433 | 142.34979675618783 | 960/960 | 99/99 | 0.23625529822967847 |
| 13 | 49.085921073898248 | 142.74528706107719 | 960/960 | 99/99 | 0.28127223595059903 |
| 14 | 49.006483748359997 | 143.08789103771502 | 960/960 | 99/99 | 0.33024265620864013 |
| 15 | 48.950727806187565 | 143.3754385281816 | 960/960 | 99/99 | 0.38317604086207824 |
| 16 | 48.929980875272484 | 143.86817890917729 | 960/960 | 99/99 | 0.44008252370431261 |
| 17 | 48.958167499809988 | 144.37730636954473 | 960/960 | 99/99 | 0.50097286441913691 |
| 18 | 49.05219686454582 | 144.8670260896784 | 960/960 | 99/99 | 0.56585842018061505 |
| 19 | 49.23233567342831 | 145.33616736118549 | 960/960 | 99/99 | 0.63475111406997664 |
| 20 | 49.522530265673332 | 145.78409162180779 | 960/960 | 99/99 | 0.70766340146455931 |
| 21 | 49.950631191209126 | 146.21085886726647 | 960/960 | 99/99 | 0.78460823264618773 |

### cold:final

- **Source**: Cold-start endpoint
- **Parent**: —
- **Acceptance**: Qualified at original specification
- **Outcome code**: Completed [Completed]
- **Elements**: 8
- **Reference / Stop / Image Positions**: 0 / 0 / 65 mm
- **Clear Stop Semi-Diameter (mm)**: 3.125

| Element / Surface | z mm | Material | Surface profile | Clear Semi-Diameter mm | Mechanical semi-diameter mm | c mm⁻¹ | k | Even Asphere Terms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| generated-0:front | 0.42230470731008962 | N-BK7 | EvenAsphere | 5 | 5 | -0.072687627630622406 | 0 | r4=-3.1999999999999999E-05; r6=-1.0083865252042736E-08 |
| generated-0:back | 1.931601937415051 | Air | EvenAsphere | 5 | 5 | -0.0098835397274240882 | 0 | r4=1.1981900888828898E-05; r6=2.6269722961748997E-09 |
| generated-1:front | 2.066084945674024 | N-F2 | EvenAsphere | 5 | 5 | -0.01263542855709463 | 0 | r4=-9.7576980535600643E-06; r6=-1.9839016887037749E-09 |
| generated-1:back | 3.3578830485566975 | Air | EvenAsphere | 5 | 5 | -0.0060035796147400369 | 0 | r4=-4.114600700514348E-06; r6=2.1876008077422332E-10 |
| generated-2:front | 3.518042425678892 | N-BK7 | EvenAsphere | 5 | 5 | -0.0065900688541994405 | 0 | r4=7.1075365967821743E-06; r6=1.5535006567562462E-10 |
| generated-2:back | 4.3427714596272224 | Air | EvenAsphere | 5 | 5 | -0.041390550229016887 | 0 | r4=1.1133670444951489E-06; r6=-6.4770522548475613E-10 |
| generated-3:front | 4.4683009605703985 | N-F2 | EvenAsphere | 5 | 5 | -0.008532577379467202 | 0 | r4=8.5216921196377692E-06; r6=6.4757151809067226E-10 |
| generated-3:back | 6.3626757324370242 | Air | EvenAsphere | 5 | 5 | -0.034428315886787443 | 0 | r4=-6.9412825210738727E-06; r6=-1.1929157074087796E-09 |
| generated-4:front | 6.8705440367110064 | N-BK7 | EvenAsphere | 5 | 5 | -0.0011138665733055055 | 0 | r4=1.485869491211312E-05; r6=1.9125005943622996E-09 |
| generated-4:back | 8.745704055093336 | Air | EvenAsphere | 5 | 5 | -0.043841723156710539 | 0 | r4=-1.0224584248710071E-05; r6=-2.0824288641894362E-09 |
| generated-5:front | 8.8966978045836136 | N-F2 | EvenAsphere | 5 | 5 | 0.0037827970618371808 | 0 | r4=6.5155355174493295E-06; r6=1.5150458653696192E-09 |
| generated-5:back | 10.901489091129823 | Air | EvenAsphere | 5 | 5 | -0.017469733482168688 | 0 | r4=-2.8021530999659084E-06; r6=-1.3798691514285023E-09 |
| generated-6:front | 11.286895535835288 | N-BK7 | EvenAsphere | 5 | 5 | 0.0061822131685786682 | 0 | r4=-5.0933524788400034E-06; r6=2.4081809979144466E-09 |
| generated-6:back | 13.221238725594205 | Air | EvenAsphere | 5 | 5 | 0.014482451783158911 | 0 | r4=5.8831481339419875E-06; r6=-6.8969645244499652E-11 |
| generated-7:front | 13.713642584021656 | N-F2 | EvenAsphere | 5 | 5 | -0.020275085670629731 | 0 | r4=-5.3828485393541194E-06; r6=-6.5464190732827016E-11 |
| generated-7:back | 15.292008075392838 | Air | EvenAsphere | 5 | 5 | 0.0078366260963341552 | 0 | r4=3.1999999999999999E-05; r6=4.5603689353516439E-09 |
- **Acceptance protocol**: 320f8825d4dba6415cfbe846be3712461f4ac39f9ba57c7d087c75e44980232e
- **Actual focal length / F-number**: 50.968399607312655 mm / 8.1549439371700245

| Field number | RMS µm | Maximum Radius µm | Valid / Attempted | Boundary / Center | Distortion % |
| --- | --- | --- | --- | --- | --- |
| 1 | 49.915503519408425 | 135.13725385288254 | 960/960 | 99/99 | 0 |
| 2 | 49.908249765642601 | 136.58887397255259 | 960/960 | 99/99 | 0.001948536150955615 |
| 3 | 49.886568811560601 | 137.84871132422992 | 960/960 | 99/99 | 0.0077945466280237704 |
| 4 | 49.850710576392387 | 138.91287163137943 | 960/960 | 99/99 | 0.017539236663393565 |
| 5 | 49.801121848245074 | 139.77619959391109 | 960/960 | 99/99 | 0.031184612169124003 |
| 6 | 49.738492602948462 | 140.43226238692876 | 960/960 | 99/99 | 0.048733475293744201 |
| 7 | 49.663822718435938 | 140.87332891991844 | 960/960 | 99/99 | 0.070189419179383955 |
| 8 | 49.578510942335079 | 141.13250973982525 | 960/960 | 99/99 | 0.09555681887029116 |
| 9 | 49.484468462752204 | 141.39429364868477 | 960/960 | 99/99 | 0.12484082310332478 |
| 10 | 49.384259859542674 | 141.69474638867629 | 960/960 | 99/99 | 0.15804734271797519 |
| 11 | 49.281274503240418 | 141.97682030667499 | 960/960 | 99/99 | 0.19518303690447472 |
| 12 | 49.179931519049433 | 142.34979675618783 | 960/960 | 99/99 | 0.23625529822967847 |
| 13 | 49.085921073898248 | 142.74528706107719 | 960/960 | 99/99 | 0.28127223595059903 |
| 14 | 49.006483748359997 | 143.08789103771502 | 960/960 | 99/99 | 0.33024265620864013 |
| 15 | 48.950727806187565 | 143.3754385281816 | 960/960 | 99/99 | 0.38317604086207824 |
| 16 | 48.929980875272484 | 143.86817890917729 | 960/960 | 99/99 | 0.44008252370431261 |
| 17 | 48.958167499809988 | 144.37730636954473 | 960/960 | 99/99 | 0.50097286441913691 |
| 18 | 49.05219686454582 | 144.8670260896784 | 960/960 | 99/99 | 0.56585842018061505 |
| 19 | 49.23233567342831 | 145.33616736118549 | 960/960 | 99/99 | 0.63475111406997664 |
| 20 | 49.522530265673332 | 145.78409162180779 | 960/960 | 99/99 | 0.70766340146455931 |
| 21 | 49.950631191209126 | 146.21085886726647 | 960/960 | 99/99 | 0.78460823264618773 |

## Stages and rejection reasons


### construction:1 · Seed construction

- **Parent**: —
- **Outcome code**: OriginalAssessed [OriginalAssessed]

```json
{
  "functionEvaluations": 15,
  "gradientEvaluations": 0,
  "validationEvaluations": 1,
  "matrixEvaluations": 36,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 44520,
  "geometryProbes": 27703,
  "elapsed": "00:00:01.6671672"
}
```


### construction:2 · Seed construction

- **Parent**: —
- **Outcome code**: OriginalAssessed [OriginalAssessed]

```json
{
  "functionEvaluations": 15,
  "gradientEvaluations": 0,
  "validationEvaluations": 1,
  "matrixEvaluations": 36,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 44520,
  "geometryProbes": 27703,
  "elapsed": "00:00:01.0520946"
}
```


### construction:3 · Seed construction

- **Parent**: —
- **Outcome code**: OriginalAssessed [OriginalAssessed]

```json
{
  "functionEvaluations": 15,
  "gradientEvaluations": 0,
  "validationEvaluations": 1,
  "matrixEvaluations": 42,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 44520,
  "geometryProbes": 31965,
  "elapsed": "00:00:01.0043032"
}
```


### construction:4 · Seed construction

- **Parent**: —
- **Outcome code**: Rejected:This thick layout has no geometrically feasible realization within its proposal budget. [Rejected:This thick layout has no geometrically feasible realization within its proposal budget.]

```json
{
  "functionEvaluations": 13,
  "gradientEvaluations": 0,
  "validationEvaluations": 0,
  "matrixEvaluations": 18,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 0,
  "geometryProbes": 12786,
  "elapsed": "00:00:00.0275513"
}
```


### construction:5 · Seed construction

- **Parent**: —
- **Outcome code**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "functionEvaluations": 0,
  "gradientEvaluations": 0,
  "validationEvaluations": 0,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 0,
  "geometryProbes": 0,
  "elapsed": "00:00:00.0017194"
}
```


### construction:6 · Seed construction

- **Parent**: —
- **Outcome code**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "functionEvaluations": 0,
  "gradientEvaluations": 0,
  "validationEvaluations": 0,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 0,
  "geometryProbes": 0,
  "elapsed": "00:00:00.0017766"
}
```


### construction:7 · Seed construction

- **Parent**: —
- **Outcome code**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "functionEvaluations": 0,
  "gradientEvaluations": 0,
  "validationEvaluations": 0,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 0,
  "geometryProbes": 0,
  "elapsed": "00:00:00.0017026"
}
```


### construction:8 · Seed construction

- **Parent**: —
- **Outcome code**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "functionEvaluations": 0,
  "gradientEvaluations": 0,
  "validationEvaluations": 0,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 0,
  "geometryProbes": 0,
  "elapsed": "00:00:00.0015651"
}
```


### construction:9 · Seed construction

- **Parent**: —
- **Outcome code**: Rejected:No physical space for this descriptor around the fixed stop. [Rejected:No physical space for this descriptor around the fixed stop.]

```json
{
  "functionEvaluations": 0,
  "gradientEvaluations": 0,
  "validationEvaluations": 0,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 0,
  "geometryProbes": 0,
  "elapsed": "00:00:00.0020388"
}
```


### compensation:0 · Sparse spherical compensation

- **Parent**: —
- **Outcome code**: Budget Exhausted [BudgetExhausted]
- **Reason**: Gate attempt 1: before=(0, 98.75743682194769, 0.8376700910881646), after=(0, 28.681132249104287, 5.017611112215532), rank=32, rate=0.01, accepted=True.; Gate attempt 2: before=(0, 28.681132249104287, 5.017611112215532), after=(0, 9.0401033470048, 5.428740677167934), rank=32, rate=0.01, accepted=True.; Gate attempt 3: before=(0, 9.0401033470048, 5.428740677167934), after=(0, 7.168151467334055, 5.980164706147932), rank=32, rate=0.01, accepted=True.; Gate attempt 4: before=(0, 7.168151467334055, 5.980164706147932), after=(0, 6.6603989199088165, 5.70582141345552), rank=32, rate=0.01, accepted=True.; Gate attempt 5: before=(0, 6.6603989199088165, 5.70582141345552), after=(0, 6.261522569914779, 5.413601867714172), rank=32, rate=0.01, accepted=True.; Gate attempt 6: before=(0, 6.261522569914779, 5.413601867714172), after=(0, 5.899910491126799, 5.14043000166661), rank=32, rate=0.01, accepted=True.; Gate attempt 7: before=(0, 5.899910491126799, 5.14043000166661), after=(0, 5.566799403609355, 4.886183815553956), rank=32, rate=0.01, accepted=True.; Gate attempt 8: before=(0, 5.566799403609355, 4.886183815553956), after=(0, 5.258226746088923, 4.648017206184292), rank=32, rate=0.01, accepted=True.; Gate attempt 9: before=(0, 5.258226746088923, 4.648017206184292), after=(0, 4.971176648187641, 4.423628887003433), rank=32, rate=0.01, accepted=True.; Gate attempt 10: before=(0, 4.971176648187641, 4.423628887003433), after=(0, 4.7032142199367755, 4.211261409252072), rank=32, rate=0.01, accepted=True.; Gate attempt 11: before=(0, 4.7032142199367755, 4.211261409252072), after=(0, 4.4523519557468205, 4.009575413629666), rank=32, rate=0.01, accepted=True.; Gate attempt 12: before=(0, 4.4523519557468205, 4.009575413629666), after=(0, 4.216956877132667, 3.817550138926378), rank=32, rate=0.01, accepted=True.; Gate attempt 13: before=(0, 4.216956877132667, 3.817550138926378), after=(0, 3.9956794482985467, 3.6344078824386328), rank=32, rate=0.01, accepted=True.; Gate attempt 14: before=(0, 3.9956794482985467, 3.6344078824386328), after=(0, 3.787397702005203, 3.459555418524757), rank=32, rate=0.01, accepted=True.; Gate attempt 15: before=(0, 3.787397702005203, 3.459555418524757), after=(0, 3.5911724921305836, 3.3561251644932795), rank=32, rate=0.01, accepted=True.; Gate attempt 16: before=(0, 3.5911724921305836, 3.3561251644932795), after=(0, 3.42504125780673, 3.253930686226091), rank=32, rate=0.01, accepted=True.; Gate attempt 17: before=(0, 3.42504125780673, 3.253930686226091), after=(0, 3.301428512896107, 3.1279632586747397), rank=32, rate=0.01, accepted=True.; Gate attempt 18: before=(0, 3.301428512896107, 3.1279632586747397), after=(0, 3.1865274981674125, 3.0050341019836186), rank=32, rate=0.01, accepted=True.; Gate attempt 19: before=(0, 3.1865274981674125, 3.0050341019836186), after=(0, 3.077733767803992, 2.8868459461819596), rank=32, rate=0.01, accepted=True.; Gate attempt 20: before=(0, 3.077733767803992, 2.8868459461819596), after=(0, 2.9744423109139486, 2.773170197777038), rank=32, rate=0.01, accepted=True.; Gate attempt 21: before=(0, 2.9744423109139486, 2.773170197777038), after=(0, 2.876203276073744, 2.6636733331619036), rank=32, rate=0.01, accepted=True.; Gate attempt 22: before=(0, 2.876203276073744, 2.6636733331619036), after=(0, 2.759997169540043, 2.526969780072338), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 23: original=(3.444135005563101E-08, 2.6522435004490905, 2.3905667553708834).; Gate attempt 23: before=(0, 2.759997169540043, 2.526969780072338), after=(0, 2.6522286875628125, 2.391016669973724), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 24: original=(7.184663860813847E-10, 2.621531526729915, 2.2500740410371103).; Gate attempt 24: before=(0, 2.6522286875628125, 2.391016669973724), after=(0, 2.621529400516868, 2.2501367336154687), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 25: original=(6.907768783409204E-10, 2.5492632714791195, 2.162398521506416).; Gate attempt 25: before=(0, 2.621529400516868, 2.2501367336154687), after=(0, 2.549261224302969, 2.1624586802903942), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 26: original=(6.377850579391064E-10, 2.476937373699866, 2.0798230988566493).; Gate attempt 26: before=(0, 2.549261224302969, 2.1624586802903942), after=(0, 2.4769354397497643, 2.0798796930837646), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 27: original=(5.887711032992446E-10, 2.3984619872613124, 2.0070856892581896).; Gate attempt 27: before=(0, 2.4769354397497643, 2.0798796930837646), after=(0, 2.398460162379899, 2.007139046334455), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 28: original=(5.30439951312058E-10, 2.3241500964207513, 1.9340611110105548).; Gate attempt 28: before=(0, 2.398460162379899, 2.007139046334455), after=(0, 2.3241483945224126, 1.9341107493015146), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 29: original=(4.735333827480602E-10, 2.2516090972423437, 1.8628811274719839).; Gate attempt 29: before=(0, 2.3241483945224126, 1.9341107493015146), after=(0, 2.2516075175366277, 1.862927076064992), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 30: original=(4.178137918512234E-10, 2.180626819397892, 1.7935082064973127).; Gate attempt 30: before=(0, 2.2516075175366277, 1.862927076064992), after=(0, 2.180625361994304, 1.793550473876974), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 31: original=(3.6415312999050476E-10, 2.111118946574756, 1.7258071299815412).; Gate attempt 31: before=(0, 2.180625361994304, 1.793550473876974), after=(0, 2.111117610584238, 1.725845755488737), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 32: original=(3.1334438823852523E-10, 2.0430202160000284, 1.6596531679969484).; Gate attempt 32: before=(0, 2.111117610584238, 1.725845755488737), after=(0, 2.043018999495239, 1.6596882224849299), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 33: original=(2.6603123028977854E-10, 1.97627797491522, 1.5949372395964485).; Gate attempt 33: before=(0, 2.043018999495239, 1.6596882224849299), after=(0, 1.9762768749935111, 1.5949688232464458), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 34: original=(2.2269894844937456E-10, 1.910851186957844, 1.531565789742363).; Gate attempt 34: before=(0, 1.9762768749935111, 1.5949688232464458), after=(0, 1.910850199815266, 1.5315940295279353), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 35: original=(1.8367328504493496E-10, 1.8467098042797447, 1.4694604619603466).; Gate attempt 35: before=(0, 1.910850199815266, 1.5315940295279353), after=(0, 1.8467089252923183, 1.4694855088764887), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 36: original=(1.4912429426608964E-10, 1.7838342284765094, 1.4085578002778665).; Gate attempt 36: before=(0, 1.8467089252923183, 1.4694855088764887), after=(0, 1.783833452290095, 1.4085798263120697), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 37: original=(1.1907535960241693E-10, 1.7221369444418457, 1.3488089643512813).; Gate attempt 37: before=(0, 1.783833452290095, 1.4085798263120697), after=(0, 1.7221362629170665, 1.3488281591989035), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 38: original=(9.542721532877067E-11, 1.6539763020503702, 1.296925802418048).; Gate attempt 38: before=(0, 1.7221362629170665, 1.3488281591989035), after=(0, 1.6539757040109777, 1.2969426126390002), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 39: original=(7.271754471344657E-11, 1.5896608156312613, 1.243096774471467).; Gate attempt 39: before=(0, 1.6539757040109777, 1.2969426126390002), after=(0, 1.5896603036699974, 1.243111097460958), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 40: original=(5.45828690480245E-11, 1.5265707498242747, 1.1899609778499292).; Gate attempt 40: before=(0, 1.5896603036699974, 1.243111097460958), after=(0, 1.5265703150745753, 1.1899730792291094), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 41: original=(4.001269238122488E-11, 1.4645799730274802, 1.1376514256277184).; Gate attempt 41: before=(0, 1.5265703150745753, 1.1899730792291094), after=(0, 1.4645796078405235, 1.1376615211707102), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 42: original=(2.929764438877721E-11, 1.3975913151852775, 1.0914333994664538).; Gate attempt 42: before=(0, 1.4645796078405235, 1.1376615211707102), after=(0, 1.3975910084891126, 1.0914418402395298), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 43: original=(2.012790141474355E-11, 1.3325075543181044, 1.0447928791131973).; Gate attempt 43: before=(0, 1.3975910084891126, 1.0914418402395298), after=(0, 1.3325073048689646, 1.044799705162053), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 44: original=(1.3403641439599054E-11, 1.2684304139192881, 0.9987261796163776).; Gate attempt 44: before=(0, 1.3325073048689646, 1.044799705162053), after=(0, 1.2684302142872823, 0.99873160979914), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 45: original=(8.579228320436951E-12, 1.2053699071841075, 0.9533164996010017).; Gate attempt 45: before=(0, 1.2684302142872823, 0.99873160979914), after=(0, 1.205369750648563, 0.9533207312082297), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 46: original=(5.249075784709511E-12, 1.143393559769916, 0.9086104756706482).; Gate attempt 46: before=(0, 1.205369750648563, 0.9533207312082297), after=(0, 1.1433934398359624, 0.9086136971908407), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 47: original=(3.0506802706846265E-12, 1.0825802482830869, 0.8646611909756328).; Gate attempt 47: before=(0, 1.1433934398359624, 0.9086136971908407), after=(0, 1.0825801587707304, 0.8646635796280724), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 48: original=(1.6717807923618294E-12, 1.0230167602337552, 0.8215295810377944).; Gate attempt 48: before=(0, 1.0825801587707304, 0.8646635796280724), after=(0, 1.023016695385176, 0.8215312999527701), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 49: original=(8.566367793807347E-13, 0.9647965227977922, 0.7792832647625535).; Gate attempt 49: before=(0, 1.023016695385176, 0.8215312999527701), after=(0, 0.9647964773699993, 0.7792844606779253), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 50: original=(4.0703090506431495E-13, 0.9080181967802013, 0.7379950162248906).; Gate attempt 50: before=(0, 0.9647964773699993, 0.7792844606779253), after=(0, 0.9080181661171958, 0.7379958178450404), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 51: original=(1.7848573841444783E-13, 0.8527840059794469, 0.6977168315879695).; Gate attempt 51: before=(0, 0.9080181661171958, 0.7379958178450404), after=(0, 0.8527839860605234, 0.697717349961014), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 52: original=(5.917552060187621E-14, 0.796120072307914, 0.6508749227544499).; Gate attempt 52: before=(0, 0.8527839860605234, 0.697717349961014), after=(0, 0.7961200609825562, 0.650875214651214), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 53: original=(2.1815582133694104E-14, 0.7400977916367203, 0.6061254789109732).; Gate attempt 53: before=(0, 0.7961200609825562, 0.650875214651214), after=(0, 0.7400977848066722, 0.6061256535025287), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 54: original=(1.600058176842259E-14, 0.6861036188998706, 0.563008896135632).; Gate attempt 54: before=(0, 0.7400977848066722, 0.6061256535025287), after=(0, 0.6861036131848204, 0.5630090410434401), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 55: original=(2.879965280061562E-14, 0.634350032190104, 0.5215570008525963).; Gate attempt 55: before=(0, 0.6861036131848204, 0.5630090410434401), after=(0, 0.6343500249292722, 0.5215571834618599), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 56: original=(6.695197073087856E-14, 0.5849544707882088, 0.4818787456375356).; Gate attempt 56: before=(0, 0.6343500249292722, 0.5215571834618599), after=(0, 0.5849544603301066, 0.4818790065287499), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 57: original=(1.4095331159600285E-13, 0.538018685233743, 0.44407551887986685).; Gate attempt 57: before=(0, 0.5849544603301066, 0.4818790065287499), after=(0, 0.5380186708175477, 0.44407587561665285), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 58: original=(2.532981671366739E-13, 0.4936293568125954, 0.40823356453696336).; Gate attempt 58: before=(0, 0.5380186708175477, 0.44407587561665285), after=(0, 0.4936293383992158, 0.4082340165554099), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 59: original=(3.946612327940222E-13, 0.4518549636158568, 0.3765307073537099).; Gate attempt 59: before=(0, 0.4936293383992158, 0.4082340165554099), after=(0, 0.4518549417044614, 0.3765312493951079), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 60: original=(1.8502566076925938E-13, 0.4100865230704457, 0.3549735041390982).; Gate attempt 60: before=(0, 0.4518549417044614, 0.3765312493951079), after=(0, 0.4100865087180972, 0.3549738556257897), rank=32, rate=0.01, accepted=True.; Hard-constraint correction attempt 61: original=(1.0594286174233833E-12, 0.37637068915888755, 0.3205209686892141).; Gate attempt 61: before=(0, 0.4100865087180972, 0.3549738556257897), after=(0, 0.3763706567428959, 0.32052174976408704), rank=32, rate=0.01, accepted=True.

```json
{
  "functionEvaluations": 102,
  "gradientEvaluations": 101,
  "validationEvaluations": 1,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 134814,
  "geometryProbes": 217362,
  "elapsed": "00:00:23.3453746"
}
```


### compensation:1 · Sparse fourth-order compensation

- **Parent**: —
- **Outcome code**: Phase Converged [Converged]
- **Reason**: Hard-constraint correction attempt 1: original=(3.656006282263109E-11, 0.06630147078083315, 0.03289980666285377).; Gate attempt 1: before=(0, 0.37637065689904164, 0.32052174957976426), after=(0, 0.06630140926326683, 0.03290099566551925), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 2: original=(1.2668681767915306E-14, 0.020636078623133176, 0.007847261876207696).; Gate attempt 2: before=(0, 0.06630140926326683, 0.03290099566551925), after=(0, 0.02063607814824876, 0.00784727285795981), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 3: original=(4.3685707310032595E-13, 0.001991323351444392, 0.003722167986223438).; Gate attempt 3: before=(0, 0.02063607814824876, 0.00784727285795981), after=(0, 0.0019913227432513153, 0.0037221938107787926), rank=48, rate=0.01, accepted=True.; Gate attempt 4: before=(0, 0.0019913227432513153, 0.0037221938107787926), after=(0, 0.0007306285426977572, 0.000537431412688008), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 5: original=(1.584134120511193E-16, 0.0002814337609075354, 0.00012587335674052303).; Gate attempt 5: before=(0, 0.0007306285426977572, 0.000537431412688008), after=(0, 0.0002812903766472603, 0.00012709140476114172), rank=48, rate=0.01, accepted=True.; Hard-constraint correction attempt 6: original=(1.3897133435333413E-16, 0.00012489445461555612, 1.7461481063828652E-05).; Gate attempt 6: before=(0, 0.0002812903766472603, 0.00012709140476114172), after=(0, 0.00012489444982286406, 1.7461534315008326E-05), rank=48, rate=0.01, accepted=True.; Gate attempt 7: before=(0, 0.00012489444982286406, 1.7461534315008326E-05), after=(0, 2.031504119451069E-05, 1.986074676132509E-06), rank=48, rate=0.01, accepted=True.; Gate attempt 8: before=(0, 2.031504119451069E-05, 1.986074676132509E-06), after=(0, 3.1183153235819375E-06, 2.831350625842945E-07), rank=48, rate=0.01, accepted=True.; Gate attempt 9: before=(0, 3.1183153235819375E-06, 2.831350625842945E-07), after=(0, 4.724417738970989E-07, 4.2099003929660915E-08), rank=48, rate=0.01, accepted=True.; Gate attempt 10: before=(0, 4.724417738970989E-07, 4.2099003929660915E-08), after=(0, 7.129116474626917E-08, 6.313581730254482E-09), rank=48, rate=0.01, accepted=True.; Gate attempt 11: before=(0, 7.129116474626917E-08, 6.313581730254482E-09), after=(0, 1.0742374131794862E-08, 9.491873289319777E-10), rank=48, rate=0.01, accepted=True.; Gate attempt 12: before=(0, 1.0742374131794862E-08, 9.491873289319777E-10), after=(0, 4.1833532852629455E-28, 1.4282482794248913E-10), rank=48, rate=0.01, accepted=True.

```json
{
  "functionEvaluations": 19,
  "gradientEvaluations": 18,
  "validationEvaluations": 1,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 60612,
  "geometryProbes": 40489,
  "elapsed": "00:00:05.9571947"
}
```


### compensation:2 · Sixth-order compensation

- **Parent**: —
- **Outcome code**: Phase Converged [Converged]
- **Reason**: Gate attempt 1: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.721873819442916E-05, 1.589197203285184E-08), rank=64, rate=0.01, accepted=False.; Gate attempt 2: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.721873819442916E-05, 1.589197203285184E-08), rank=64, rate=0.005, accepted=False.; Gate attempt 3: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.721873819442916E-05, 1.589197203285184E-08), rank=64, rate=0.0025, accepted=False.; Gate attempt 4: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 8.37261938492282E-05, 1.8676136420333663E-08), rank=64, rate=0.00125, accepted=False.; Gate attempt 5: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 5.063415957596448E-05, 3.6409330916804273E-08), rank=64, rate=0.000625, accepted=False.; Gate attempt 6: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 1.9516204794473335E-05, 6.376724706592001E-08), rank=64, rate=0.0003125, accepted=False.; Gate attempt 7: before=(0, 4.1833532852629455E-28, 1.542152161824789E-05), after=(0, 3.7794310923098115E-06, 9.327859349769029E-08), rank=64, rate=0.00015625, accepted=True.; Gate attempt 8: before=(0, 3.7794310923098115E-06, 9.327859349769029E-08), after=(0, 4.1833532852629455E-28, 1.5936277393089257E-10), rank=64, rate=0.0001875, accepted=True.

```json
{
  "functionEvaluations": 10,
  "gradientEvaluations": 9,
  "validationEvaluations": 1,
  "matrixEvaluations": 0,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 101814,
  "geometryProbes": 21310,
  "elapsed": "00:00:22.8092722"
}
```


### preparation:status · Preparation completed

- **Parent**: —
- **Outcome code**: Completed [Completed]
- **Reason**: kernel=db5cbdaf597dd2460c22300f257a7b28e2fc86e38aca57c3c12dad981d15756d

```json
{
  "functionEvaluations": 189,
  "gradientEvaluations": 128,
  "validationEvaluations": 6,
  "matrixEvaluations": 132,
  "differenceEvaluations": 0,
  "jacobianBuilds": 0,
  "tracedRays": 430800,
  "geometryProbes": 379318,
  "elapsed": "00:00:55.8939597"
}
```


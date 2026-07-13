# Orbbec SDK DotNet Wrapper

This is a wrapper for the Orbbec SDK for DotNet. It supports .NET 8.0 and later versions, and is compatible with Windows and Linux platforms. 

It provides a simple and easy-to-use interface for accessing the Orbbec camera and performing various operations such as capturing depth images, color images, and performing point cloud generation.

The v2-main branch is based the open source version of [Orbbec SDK v2](https://github.com/orbbec/OrbbecSDK_v2). 


**If you need to use .NET Framework, please use [this repository](https://github.com/orbbec/OrbbecSDK_CSharp). However, .NET Framework is maintained for bug fixes only, and no new features will be added. We recommend using Orbbec SDK DotNet Wrapper instead.**

# Support Platforms

- Windows: Windows 10 (x64), tested with .NET 8

- Linux x64: Tested on Ubuntu 20.04, 22.04, and 24.04, with .NET 8

- Linux ARM64: Tested on NVIDIA Jetson Orin Nano,with .NET 8



# Supported Devices


| **Products List** | **Minimal Firmware Version** | **Recommended FW Version**    |   Notes      |
|-------------------|------------------------------|-------------------------------|-------------|
| Gemini 305        | 1.0.30                     |        1.0.30                   |
| Gemini 345        | 1.7.04                     |        1.9.03                   |
| Gemini 345Lg        | 1.7.04                     |        1.9.03                   |
| Gemini 435Le        | 1.2.4                     |        1.3.6                   |
| Gemini 335Le        | 1.5.31                     |        1.6.00                     |
| Gemini 330        | 1.2.20                       |        1.6.00                       |
| Gemini 330L       | 1.2.20                       |       1.6.00                      |
| Gemini 335        | 1.2.20                       |       1.6.00                        |
| Gemini 335L       | 1.2.20                       |        1.6.00                       |
| Gemini 336        | 1.2.20                       |       1.6.00                        |
| Gemini 336L       | 1.2.20                       |        1.6.00                       |
| Gemini 335Lg      | 1.3.46                       |        1.6.00                       |
| Femto Bolt        | 1.1.2                  |              1.1.3                       |
| Femto Mega        | 1.3.0                  |              1.3.1                       |
| Femto Mega I        | 2.0.4                  |            2.0.4                     |
| Astra 2           | 2.8.20                       |         2.8.20                      |
| Gemini 2 L        | 1.4.53                       |        1.5.2                       |
| Gemini 2          | 1.4.92               |                1.4.98                       |
| Gemini 215        | 1.0.9                        |        1.0.9                      |
| Gemini 210        | 1.0.9                        |        1.0.9                      |
| Astra mini Pro        | 2.0.03                        |        2.0.03                        |
| Astra mini S Pro        | 2.0.03                        |        2.0.03                        |
| Pulsar SL450      | 2.2.4.5                      | 2.2.4.5                          |
| Pulsar ME450      | 1.0.0.6                      | 1.0.0.6                          |


# Environment Setup

Please refer to [this document](https://orbbec.github.io/OrbbecSDK_DotNet/source/2_installation/Compilation.html#orbbecsdk-dotnet-wrapper-compilation) for compilation instructions.



## windows
For windows, you need to register the metadata associated with frames (this includes things like timestamps and other information about the video frame).

- Metadata registration follow this:[/scripts/obsensor_metadata_win10.md](scripts/obsensor_metadata_win10.md)

*Notes: If the metadata is not registered, the device timestamp will be abnormal, thereby affecting the SDK’s internal frame synchronization functionality.*


## Linux

For Linux, we have provided a script to help you set up the environment. You can run the script as follows:

```bash
cd scripts
  sudo chmod +x ./install_udev_rules.sh
  sudo ./install_udev_rules.sh
  sudo udevadm control --reload && sudo udevadm trigger
```

*Notes: If this script is not executed, open the device will fail due to permission issues. You need to run the sample with sudo (administrator privileges).*




# How to Use build_sample Script
To make dotnet wrapper more user-friendly, We provide scripts to publish the project and generate executable binaries .

## Windows
```powershell
cd /path/to/orbbecsdk_dotnet

./build_samples.ps1

cd ./bin/Release/win-x64

./Samples.Color.exe
```

## Linux
```bash
cd /path/to/orbbecsdk_dotnet

./build_samples.sh

cd ./bin/Release/linux-x64

./Samples.Color
```
![Color Example](image/compile-7.png)



# Documentation

For compilation and API reference, please refer to [UserGuide](https://orbbec.github.io/OrbbecSDK_DotNet/index.html) documentation.

# License

This project is licensed under the Apache License 2.0.

                                                                               


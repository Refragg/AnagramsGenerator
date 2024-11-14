# AnagramsGenerator

A program to generate every possible anagram for any word.  
This program can use multiple threads to accelerate the generation.

## Usage

`AnagramGenerator YOUR-INPUT-WORD`  
  
Press CTRL+C / send a SIGINT signal to cancel the current generation.  
  
You can then resume the generation by using

`AnagramGenerator YOUR-INPUT-WORD -r|--resume`

## Build

Prerequisites: Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```
git clone https://github.com/Refragg/AnagramsGenerator.git
cd AnagramsGenerator
dotnet build -c Release
```

The binary can then be located at `./AnagramsGenerator/bin/Release/net8.0`

## Note

This code may not be the prettiest or fastest, it was mostly something for me at first that I thought I may aswell share :)
#!/bin/bash

# Find all .csproj files and clean them
find . -name "*.csproj" -exec dotnet clean {} \;

#!/bin/bash
# Setup script to initialize Git and push to GitHub

set -e

echo "========================================="
echo "LoxoneSolarForecast - GitHub Setup"
echo "========================================="

# Initialize git repository if not already done
if [ ! -d ".git" ]; then
    echo "Initializing git repository..."
    git init
    git config user.name "Your Name"
    git config user.email "your.email@example.com"
fi

# Add all files
echo "Adding files to git..."
git add .

# Initial commit
echo "Creating initial commit..."
git commit -m "Initial commit: LoxoneSolarForecast - ASP.NET Core 9 Solar Forecasting with Loxone Integration and InfluxDB"

# Add remote (you'll need to replace with your GitHub repo URL)
echo ""
echo "========================================="
echo "IMPORTANT: Add your GitHub repository"
echo "========================================="
echo "Before pushing, you need to:"
echo "1. Create a NEW PRIVATE repository on GitHub"
echo "2. Run this command (replace with your repo URL):"
echo ""
echo "  git remote add origin https://github.com/YOUR_USERNAME/LoxoneSolarForecast.git"
echo "  git branch -M main"
echo "  git push -u origin main"
echo ""
echo "========================================="

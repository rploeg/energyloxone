# GitHub & Portainer Setup Guide

## Part 1: Push to Private GitHub Repository

### Step 1: Create Private Repository on GitHub
1. Go to https://github.com/new
2. Repository name: `LoxoneSolarForecast`
3. Select **Private**
4. Click "Create repository"

### Step 2: Initialize Git Locally
Open your terminal and run:

```bash
cd /Users/remco/repo/LoxoneSolarForecast

# Initialize git (if not already done)
git init

# Configure your identity
git config user.name "Your Name"
git config user.email "your.email@example.com"

# Add remote repository (replace with your GitHub repo URL)
git remote add origin https://github.com/YOUR_USERNAME/LoxoneSolarForecast.git

# Add all files
git add .

# Create initial commit
git commit -m "Initial commit: LoxoneSolarForecast - ASP.NET Core 9 Solar Forecasting with Loxone Integration and InfluxDB"

# Push to GitHub
git branch -M main
git push -u origin main
```

### Step 3: Verify on GitHub
- Go to your repository URL: `https://github.com/YOUR_USERNAME/LoxoneSolarForecast`
- You should see all project files

---

## Part 2: Push Docker Image to Registry (for Portainer)

### Option A: Docker Hub (Easiest)

```bash
cd /Users/remco/repo/LoxoneSolarForecast

# Login to Docker Hub
docker login

# Build image (if not already built)
docker-compose build

# Tag the image
docker tag loxone-solar-forecast:latest YOUR_DOCKER_USERNAME/loxone-solar-forecast:latest

# Push to Docker Hub
docker push YOUR_DOCKER_USERNAME/loxone-solar-forecast:latest
```

### Option B: Private Registry / Harbor

If you have a private registry running:

```bash
# Login to your private registry
docker login your-registry.com:5000

# Tag for your registry
docker tag loxone-solar-forecast:latest your-registry.com:5000/loxone-solar-forecast:latest

# Push
docker push your-registry.com:5000/loxone-solar-forecast:latest
```

---

## Part 3: Deploy to Portainer (x64)

### Step 1: Open Portainer
- Go to your Portainer instance (typically http://your-ip:9000)
- Login

### Step 2: Create New Stack

#### Via Docker Compose (Recommended)
1. **Stacks** → **Add Stack**
2. **Name:** `loxone-solar-forecast`
3. **Build method:** Upload or Paste
4. Use this docker-compose.yml:

```yaml
version: '3.8'

services:
  loxone-solar-forecast:
    image: YOUR_DOCKER_USERNAME/loxone-solar-forecast:latest
    container_name: loxone-solar-forecast
    ports:
      - "5001:5000"
    volumes:
      - solar-forecast-config:/config
    environment:
      - ASPNETCORE_URLS=http://+:5000
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped
    networks:
      - loxone-network

volumes:
  solar-forecast-config:
    driver: local

networks:
  loxone-network:
    driver: bridge
```

5. **Deploy Stack**

### Step 2: Monitor Container
- Go to **Containers** → Find `loxone-solar-forecast`
- Check logs to verify it's running
- Access the web UI at `http://your-portainer-host:5001`

---

## Configuration via Portainer

Once deployed:

1. **Port Binding:** Change port mapping if needed
2. **Volume Mount:** Ensure `/config` volume is persistent
3. **Restart Policy:** Set to `unless-stopped`
4. **Logs:** Monitor via Portainer Logs tab

---

## Updating in Portainer

When you push a new image version:

1. **Stacks** → Select `loxone-solar-forecast`
2. **Editor** tab
3. **Pull latest image** (if using `latest` tag)
4. **Update the Stack**
5. Portainer will redeploy with new image

---

## Environment Variables

Configure these in Portainer's stack environment:

| Variable | Value | Default |
|----------|-------|---------|
| `ASPNETCORE_URLS` | `http://+:5000` | Required |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Recommended |

---

## Backup Configuration

Your configuration files are in `/config` volume on Portainer host:

```bash
# Backup from Portainer host
docker run --rm -v solar-forecast-config:/config -v $(pwd):/backup alpine cp -r /config/* /backup/

# Or restore from backup
docker run --rm -v solar-forecast-config:/config -v $(pwd):/backup alpine cp -r /backup/* /config/
```

---

## Troubleshooting

### Image Pull Fails
- Verify Docker login: `docker login`
- Check image name: `docker images | grep loxone`
- Ensure image exists on registry

### Container Won't Start
- Check logs: **Containers** → **loxone-solar-forecast** → **Logs**
- Verify port 5001 is available
- Check volume mount permissions

### Configuration Lost
- Ensure `/config` volume is mounted
- Check volume persistent storage on host
- Backup before updates: `docker cp container:/config ./config-backup`


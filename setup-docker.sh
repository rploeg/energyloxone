#!/bin/bash
# Setup script to push Docker image to Portainer/Registry

set -e

PROJECT_NAME="loxone-solar-forecast"
DOCKER_USERNAME=${1:-"your-docker-username"}
REGISTRY_URL=${2:-"docker.io"}
IMAGE_TAG="latest"

echo "========================================="
echo "LoxoneSolarForecast - Docker Push Setup"
echo "========================================="
echo ""
echo "This script will:"
echo "1. Build the Docker image"
echo "2. Tag it for your registry"
echo "3. Push it to your registry for use in Portainer"
echo ""
echo "Usage: ./setup-docker.sh [docker-username] [registry-url]"
echo "  Default username: your-docker-username"
echo "  Default registry: docker.io"
echo ""
echo "For Docker Hub: ./setup-docker.sh myusername"
echo "For private registry: ./setup-docker.sh myusername myregistry.com:5000"
echo ""

# Build the image
echo "Step 1: Building Docker image..."
docker-compose -f docker-compose.yml build

# Tag the image
IMAGE_ID=$(docker images --filter "reference=$PROJECT_NAME:*" --format "{{.ID}}" | head -1)
FULL_TAG="$REGISTRY_URL/$DOCKER_USERNAME/$PROJECT_NAME:$IMAGE_TAG"

echo ""
echo "Step 2: Tagging image as: $FULL_TAG"
docker tag "$PROJECT_NAME:latest" "$FULL_TAG"

# Login and push
echo ""
echo "Step 3: Login to Docker registry..."
docker login -u "$DOCKER_USERNAME" "$REGISTRY_URL"

echo ""
echo "Step 4: Pushing image to registry..."
docker push "$FULL_TAG"

echo ""
echo "========================================="
echo "SUCCESS! Image pushed to: $FULL_TAG"
echo "========================================="
echo ""
echo "To use in Portainer (x64):"
echo "1. Go to Portainer → Stacks → Add Stack"
echo "2. Use this image in your docker-compose.yml:"
echo ""
echo "    image: $FULL_TAG"
echo ""
echo "Or create a new stack with this docker-compose:"
echo ""
cat << EOF
version: '3.8'
services:
  loxone-solar-forecast:
    image: $FULL_TAG
    container_name: loxone-solar-forecast
    ports:
      - "5001:5000"
    volumes:
      - solar-config:/config
    environment:
      - ASPNETCORE_URLS=http://+:5000
      - ASPNETCORE_ENVIRONMENT=Production
    restart: unless-stopped

volumes:
  solar-config:
    driver: local
EOF

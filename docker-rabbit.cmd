WHERE docker-machine >nul 2>nul
IF ERRORLEVEL 0 docker-machine start
docker container inspect rabbitmq >nul 2>nul || docker create --name rabbitmq -p 5672:5672 -p 15672:15672 -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest --label pinned=true rabbitmq:3-management
docker start rabbitmq
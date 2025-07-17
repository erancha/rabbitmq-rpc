#!/bin/bash

# Set working directory to script location
cd "$(dirname "$0")"

# Record start time
start_time=$(date +%s)

# Run JMeter test in non-GUI mode
jmeter -Djava.security.egd=file:/dev/urandom \
       -Dxstream.allow=org.apache.jmeter.save.ScriptWrapper \
       -n -t create-users-test-plan.jmx -l results.jtl

# Record end time and calculate duration
end_time=$(date +%s)
duration=$((end_time - start_time))
minutes=$((duration / 60))
seconds=$((duration % 60))

# Check if test was successful
if [ $? -eq 0 ]; then
    echo "Test completed successfully in ${minutes}m ${seconds}s!"
    echo "Results available in: ./results.jtl"

    # Check container logs for errors and warnings
    echo "Checking container logs for issues..."
    cd ..
    echo "WebAPI Errors/Warnings:"
    docker compose -p todo-app logs webapi 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
    
    echo "Worker Errors/Warnings:"
    docker compose -p todo-app logs worker 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
    
    # echo "Postgres Errors/Warnings:"
    # docker compose -p todo-app logs postgres 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
    
    # echo "RabbitMQ Errors/Warnings:"
    # docker compose -p todo-app logs rabbitmq 2>&1 | grep -i -E "error|warn|fail|exception" || echo "No issues found"
else
    echo "Test failed after ${minutes}m ${seconds}s!"
fi


# # 1. Go to /opt or another location
# cd /opt

# # 2. Download the latest JMeter (update the version if needed)
# sudo wget https://downloads.apache.org//jmeter/binaries/apache-jmeter-5.6.3.tgz

# # 3. Extract it
# sudo tar -xvzf apache-jmeter-5.6.3.tgz

# # 4. Optionally, link it for easy use
# sudo ln -s /opt/apache-jmeter-5.6.3/bin/jmeter /usr/local/bin/jmeter

# # 5. Test it
# jmeter -v

# # 6. Extract the Archive
# sudo tar -xvzf apache-jmeter-5.6.3.tgz

# # 7. Create a Global Symlink (Optional but Recommended)
# sudo ln -s /opt/apache-jmeter-5.6.3/bin/jmeter /usr/local/bin/jmeter

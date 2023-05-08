pipeline {
  agent any
  stages {
    stage('Build') {
      environment {
        version = "1.2.${env.BUILD_NUMBER}"
      }
      steps {
        sh 'dotnet  publish ./TelegramSearchBot/TelegramSearchBot.csproj -c Release -o ./cache/out -r linux-x64 --self-contained false'
      }
    }
    stage('Deploy') {
      environment {
         SERVER_CREDENTIALS=credentials('39b2412f-5b4e-454c-8731-df242cde5416')
         SERVER_IP=credentials('8fa15208-26b2-4d14-bca9-d2448f7f5240')
      }
      steps {
        sh 'sshpass -p $SERVER_CREDENTIALS_PSW ssh $SERVER_CREDENTIALS_USR@$SERVER_IP "cd /root && rm -rf TelegramSearchBot/*"'
        sh 'sshpass -p $SERVER_CREDENTIALS_PSW scp -r ./cache/out/* $SERVER_CREDENTIALS_USR@$SERVER_IP:/root/TelegramSearchBot/'
        sh 'sshpass -p $SERVER_CREDENTIALS_PSW ssh $SERVER_CREDENTIALS_USR@$SERVER_IP "pm2 stop dotnet && pm2 start dotnet"'
      }
    }
  }
}
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
  }
}